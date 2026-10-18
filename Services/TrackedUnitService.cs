using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// What a Unit Tracker write produced. Message is the toast text either way
    /// (success text with any "Heads up" appended, or the reason it was refused);
    /// Unit is set on a successful Add/Update so the controller can carry values
    /// into "Save &amp; add another".
    /// </summary>
    public class TrackedUnitResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public TrackedUnit? Unit { get; init; }
    }

    /// <summary>
    /// Every query and write for the Unit Tracker holdover list. The controller
    /// holds no LINQ of its own.
    ///
    /// Every access rule is enforced HERE or by [RequireLevel] on the action --
    /// hidden buttons are cosmetic. The one rule that can't be an attribute is
    /// Update's: Standard may edit a unit, only Engineer may move it to another
    /// Line or Team.
    ///
    /// Forms on this page post normally, but VIS forms elsewhere submit through
    /// form.submit(), which skips HTML validation (Pass 39 / VIS-12), so the
    /// server is the only check that counts: every text field is trimmed, every
    /// over-length value is REFUSED with a message naming the field (never
    /// truncated), and Package/Split, Line and Team are validated here.
    /// </summary>
    public class TrackedUnitService
    {
        private const int ModelMax = 60;
        private const int SerialMax = 60;
        private const int SampleMax = 30;
        private const int NotesMax = 2000;

        private readonly AppDbContext _db;
        private readonly CurrentUserService _user;

        public TrackedUnitService(AppDbContext db, CurrentUserService user)
        {
            _db = db;
            _user = user;
        }

        // =====================================================================
        // READS
        // =====================================================================

        /// <summary>
        /// The list, newest first. Every signed-in user sees every row (units are
        /// visible across Lines), so there is deliberately no Line scoping here.
        /// </summary>
        public List<TrackedUnit> Query(string? q, string? pkg, string? team, string? line,
            string? whereabouts = null, string? buildStatus = null)
        {
            var query = _db.TrackedUnits.AsNoTracking().AsQueryable();

            var term = (q ?? "").Trim().ToLower();
            if (term != "")
            {
                query = query.Where(u =>
                    u.ModelNumber.ToLower().Contains(term) ||
                    u.SerialNumber.ToLower().Contains(term) ||
                    u.SampleNumber.ToLower().Contains(term) ||
                    u.Notes.ToLower().Contains(term) ||
                    u.Line.ToLower().Contains(term) ||
                    u.Team.ToLower().Contains(term));
            }

            var canonicalPkg = CanonicalPackage(pkg);
            if (canonicalPkg != null) query = query.Where(u => u.PackageType == canonicalPkg);

            var teamFilter = (team ?? "").Trim();
            if (teamFilter != "") query = query.Where(u => u.Team == teamFilter);

            var lineFilter = (line ?? "").Trim();
            if (lineFilter != "") query = query.Where(u => u.Line == lineFilter);

            // "none" asks for the units nobody has said anything about; one of the
            // three labels asks for just those; anything else means Any.
            var whFilter = (whereabouts ?? "").Trim();
            if (string.Equals(whFilter, TrackedUnitWhereabouts.NotSet, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.Whereabouts == "");
            }
            else
            {
                var canonicalWh = TrackedUnitWhereabouts.Canonical(whFilter);
                if (canonicalWh != null) query = query.Where(u => u.Whereabouts == canonicalWh);
            }

            // Same three-way rule for Build: "none", one of the two labels, or Any.
            var bsFilter = (buildStatus ?? "").Trim();
            if (string.Equals(bsFilter, TrackedUnitBuildStatus.NotSet, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.BuildStatus == "");
            }
            else
            {
                var canonicalBs = TrackedUnitBuildStatus.Canonical(bsFilter);
                if (canonicalBs != null) query = query.Where(u => u.BuildStatus == canonicalBs);
            }

            return query.OrderByDescending(u => u.Id).ToList();
        }

        /// <summary>Every Team that appears on at least one row, for the Team filter.</summary>
        public List<string> TeamsInUse() =>
            _db.TrackedUnits.AsNoTracking().Where(u => u.Team != "")
                .Select(u => u.Team).Distinct().OrderBy(t => t).ToList();

        /// <summary>Every Line that appears on at least one row, for the Line filter.</summary>
        public List<string> LinesInUse() =>
            _db.TrackedUnits.AsNoTracking().Where(u => u.Line != "")
                .Select(u => u.Line).Distinct().OrderBy(l => l).ToList();

        /// <summary>Active teams, ordered by name -- the Add/Edit Team picker.</summary>
        public List<Team> ActiveTeams() =>
            _db.Teams.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToList();

        /// <summary>Active team name -> home Line, for the Team-picks-Line autofill. Teams with no Line are absent.</summary>
        public Dictionary<string, string?> TeamLines() =>
            _db.Teams.AsNoTracking().Where(t => t.IsActive && t.Line != null)
                .ToDictionary(t => t.Name, t => t.Line);

        // =====================================================================
        // WRITES
        // =====================================================================

        public TrackedUnitResult Add(string? modelNumber, string? packageType, string? serialNumber,
            string? sampleNumber, string? line, string? team, string? notes)
        {
            var model = Clean(modelNumber);
            var serial = Clean(serialNumber);
            var sample = Clean(sampleNumber);
            var noteText = Clean(notes);

            if (model == "") return Fail("Model number is required.");
            var err = TooLong("Model number", model, ModelMax)
                      ?? TooLong("Serial #", serial, SerialMax)
                      ?? TooLong("Sample #", sample, SampleMax)
                      ?? TooLong("Notes", noteText, NotesMax);
            if (err != null) return Fail(err);

            var pkg = CanonicalPackage(packageType);
            if (pkg == null) return Fail("Package / Split must be Package or Split.");

            var lineClean = Clean(line);
            if (lineClean == "") return Fail("Line is required.");
            var canonLine = CanonicalLine(lineClean);
            if (canonLine == null) return Fail($"Line '{lineClean}' isn't an active Line.");

            var teamResolved = ResolveTeam(team, out var teamErr);
            if (teamResolved == null) return Fail(teamErr!);

            var now = DateTime.UtcNow;
            var unit = new TrackedUnit
            {
                ModelNumber = model,
                PackageType = pkg,
                SerialNumber = serial,
                SampleNumber = sample,
                Line = canonLine,
                Team = teamResolved,
                Notes = noteText,
                RecordedAt = now,
                RecordedBy = _user.Name,
                UpdatedAt = null,
                UpdatedBy = ""
            };

            // The unit's Id is assigned by the INSERT, and the log's Details name
            // it ("Unit #12"), so the log can't ride in the very same SaveChanges
            // the way Update/Notes/Delete do. One transaction instead: either both
            // rows land or neither does.
            using var tx = _db.Database.BeginTransaction();
            _db.TrackedUnits.Add(unit);
            _db.SaveChanges();
            Log("Tracked Unit Added", +1,
                $"Unit #{unit.Id} · {model} · {pkg}"
                + (serial != "" ? $" · Serial {serial}" : "")
                + (sample != "" ? $" · {SampleLabel(sample)}" : "")
                + $" · {Place(canonLine)}"
                + (teamResolved != "" ? $" · {teamResolved}" : ""));
            _db.SaveChanges();
            tx.Commit();

            return new TrackedUnitResult
            {
                Ok = true,
                Unit = unit,
                Message = $"Unit #{unit.Id} added ({model}, {pkg})." + DuplicateWarnings(unit)
            };
        }

        /// <summary>
        /// Standard and up. line/team are null when the form didn't post them (a
        /// Standard user's disabled selects), which means "leave alone". A posted
        /// Line or Team that differs from the stored one is a MOVE and needs
        /// Engineer; it is validated only when it actually changed, so a Line or
        /// Team hidden in Settings since the row was saved can still be carried
        /// through an untouched Save.
        /// </summary>
        public TrackedUnitResult Update(int id, string? modelNumber, string? packageType, string? serialNumber,
            string? sampleNumber, string? line, string? team, string? notes)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);

            string? lineChange = null, teamChange = null;
            if (line != null)
            {
                var l = Clean(line);
                if (!string.Equals(l, unit.Line, StringComparison.Ordinal)) lineChange = l;
            }
            if (team != null)
            {
                var t = Clean(team);
                if (t.Equals("N/A", StringComparison.OrdinalIgnoreCase)) t = "";
                if (!string.Equals(t, unit.Team, StringComparison.Ordinal)) teamChange = t;
            }

            if ((lineChange != null || teamChange != null) && _user.Level < AccessLevels.Engineer)
                return Fail("Moving a unit to another Line or Team needs Engineer access.");

            var model = Clean(modelNumber);
            var serial = Clean(serialNumber);
            var sample = Clean(sampleNumber);
            var noteText = Clean(notes);

            if (model == "") return Fail("Model number is required.");
            var err = TooLong("Model number", model, ModelMax)
                      ?? TooLong("Serial #", serial, SerialMax)
                      ?? TooLong("Sample #", sample, SampleMax)
                      ?? TooLong("Notes", noteText, NotesMax);
            if (err != null) return Fail(err);

            var pkg = CanonicalPackage(packageType);
            if (pkg == null) return Fail("Package / Split must be Package or Split.");

            string newLine = unit.Line, newTeam = unit.Team;
            if (lineChange != null)
            {
                if (lineChange == "") return Fail("Line is required.");
                var canon = CanonicalLine(lineChange);
                if (canon == null) return Fail($"Line '{lineChange}' isn't an active Line.");
                newLine = canon;
            }
            if (teamChange != null)
            {
                var resolved = ResolveTeam(teamChange, out var teamErr);
                if (resolved == null) return Fail(teamErr!);
                newTeam = resolved;
            }

            // Table-column order. Notes text is never logged -- it can be long and
            // it is free for everyone to write.
            var changes = new List<string>();
            void Diff(string label, string oldV, string newV, Action apply)
            {
                if (string.Equals(oldV, newV, StringComparison.Ordinal)) return;
                changes.Add($"{label} '{oldV}' → '{newV}'");
                apply();
            }
            Diff("Model", unit.ModelNumber, model, () => unit.ModelNumber = model);
            Diff("Pkg/Split", unit.PackageType, pkg, () => unit.PackageType = pkg);
            Diff("Serial", unit.SerialNumber, serial, () => unit.SerialNumber = serial);
            Diff("Sample", unit.SampleNumber, sample, () => unit.SampleNumber = sample);
            Diff("Line", unit.Line, newLine, () => unit.Line = newLine);
            Diff("Team", unit.Team, newTeam, () => unit.Team = newTeam);
            if (!string.Equals(unit.Notes, noteText, StringComparison.Ordinal))
            {
                changes.Add("Notes edited");
                unit.Notes = noteText;
            }

            if (changes.Count == 0)
                return new TrackedUnitResult { Ok = true, Unit = unit, Message = "Nothing changed." };

            unit.UpdatedAt = DateTime.UtcNow;
            unit.UpdatedBy = _user.Name;
            Log("Tracked Unit Updated", 0, $"Unit #{unit.Id} ({unit.ModelNumber}): {string.Join("; ", changes)}.");

            if (!TrySave()) return Gone(id);
            return new TrackedUnitResult
            {
                Ok = true,
                Unit = unit,
                Message = $"Unit #{unit.Id} updated ({unit.ModelNumber}, {unit.PackageType})." + DuplicateWarnings(unit)
            };
        }

        /// <summary>Anyone signed in, Viewer included. Changes the Notes and nothing else.</summary>
        public TrackedUnitResult UpdateNotes(int id, string? notes)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);

            var noteText = Clean(notes);
            var err = TooLong("Notes", noteText, NotesMax);
            if (err != null) return Fail(err);

            if (string.Equals(unit.Notes, noteText, StringComparison.Ordinal))
                return new TrackedUnitResult { Ok = true, Unit = unit, Message = "Nothing changed." };

            unit.Notes = noteText;
            unit.UpdatedAt = DateTime.UtcNow;
            unit.UpdatedBy = _user.Name;
            Log("Tracked Unit Notes Edited", 0, $"Unit #{unit.Id} ({unit.ModelNumber}): notes edited.");

            if (!TrySave()) return Gone(id);
            return new TrackedUnitResult { Ok = true, Unit = unit, Message = $"Unit #{unit.Id} notes saved." };
        }

        /// <summary>
        /// Standard and up (the action carries [RequireLevel]). Sets or clears where
        /// a unit is: one of In Lab / In Storage / Outbound, or blank for "not
        /// recorded". Touches the Whereabouts column and nothing else, so it can't
        /// move a unit's Line or Team. Posting the value it already has changes
        /// nothing and writes no log. The message deliberately carries no typed
        /// text (the page shows it in a toast built with innerHTML).
        /// </summary>
        public TrackedUnitResult SetWhereabouts(int id, string? value)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);

            var raw = Clean(value);
            var target = raw == "" ? "" : TrackedUnitWhereabouts.Canonical(raw);
            if (target == null) return Fail("Where must be In Lab, In Storage or Outbound.");

            if (string.Equals(unit.Whereabouts, target, StringComparison.Ordinal))
                return new TrackedUnitResult { Ok = true, Unit = unit, Message = "Nothing changed." };

            var old = unit.Whereabouts;
            unit.Whereabouts = target;
            unit.UpdatedAt = DateTime.UtcNow;
            unit.UpdatedBy = _user.Name;
            Log("Tracked Unit Whereabouts Changed", 0,
                $"Unit #{unit.Id} ({unit.ModelNumber}): Whereabouts '{old}' → '{target}'.");

            if (!TrySave()) return Gone(id);
            return new TrackedUnitResult
            {
                Ok = true,
                Unit = unit,
                Message = target == "" ? $"Unit #{unit.Id}: whereabouts cleared." : $"Unit #{unit.Id}: {target}."
            };
        }

        /// <summary>
        /// Standard and up (the action carries [RequireLevel]). Sets or clears whether
        /// a unit has been built: "Built" or "Not Built", or blank for "not recorded".
        /// Touches the BuildStatus column and nothing else (Whereabouts, Line and Team
        /// are untouched), and works exactly like SetWhereabouts: only the two values or
        /// blank, a value the unit already has changes nothing and writes no log, and
        /// the message carries no typed text.
        /// </summary>
        public TrackedUnitResult SetBuildStatus(int id, string? value)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);

            var raw = Clean(value);
            var target = raw == "" ? "" : TrackedUnitBuildStatus.Canonical(raw);
            if (target == null) return Fail("Build must be Built or Not Built.");

            if (string.Equals(unit.BuildStatus, target, StringComparison.Ordinal))
                return new TrackedUnitResult { Ok = true, Unit = unit, Message = "Nothing changed." };

            var old = unit.BuildStatus;
            unit.BuildStatus = target;
            unit.UpdatedAt = DateTime.UtcNow;
            unit.UpdatedBy = _user.Name;
            Log("Tracked Unit Build Status Changed", 0,
                $"Unit #{unit.Id} ({unit.ModelNumber}): Build status '{old}' → '{target}'.");

            if (!TrySave()) return Gone(id);
            return new TrackedUnitResult
            {
                Ok = true,
                Unit = unit,
                Message = target == "" ? $"Unit #{unit.Id}: build status cleared." : $"Unit #{unit.Id}: {target}."
            };
        }

        /// <summary>
        /// Engineer and up (the action carries [RequireLevel]). A hard delete of the
        /// row; the log row written here is the record, so it says what was removed.
        /// </summary>
        public TrackedUnitResult Delete(int id)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);

            Log("Tracked Unit Deleted", -1,
                $"Unit #{unit.Id} deleted · {unit.ModelNumber} · {unit.PackageType}"
                + (unit.SerialNumber != "" ? $" · Serial {unit.SerialNumber}" : "")
                + (unit.SampleNumber != "" ? $" · {SampleLabel(unit.SampleNumber)}" : "")
                + $" · {unit.Line}"
                + (unit.Team != "" ? $" · {unit.Team}" : ""));
            _db.TrackedUnits.Remove(unit);

            if (!TrySave()) return Gone(id);
            return new TrackedUnitResult
            {
                Ok = true,
                Message = $"Unit #{unit.Id} deleted ({unit.ModelNumber}, {unit.PackageType})."
            };
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private static TrackedUnitResult Fail(string message) => new() { Ok = false, Message = message };

        private static TrackedUnitResult Gone(int id) => Fail($"Unit #{id} no longer exists.");

        /// <summary>
        /// Trim, and fold line breaks to "\n". A textarea's maxlength counts a break
        /// as one character but the browser posts it as "\r\n"; counting two would
        /// refuse a Notes box the form just accepted.
        /// </summary>
        private static string Clean(string? s) =>
            (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        private static string? TooLong(string label, string value, int max) =>
            value.Length > max ? $"{label} can be at most {max} characters (this is {value.Length})." : null;

        private static string? CanonicalPackage(string? value)
        {
            var v = (value ?? "").Trim();
            return TrackedUnitPackage.All.FirstOrDefault(p => string.Equals(p, v, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The vocabulary's own spelling of a Line, or null if it isn't an active Line.</summary>
        private static string? CanonicalLine(string line)
        {
            if (!OrgStructure.IsValidLine(line)) return null;
            return OrgStructure.AllLines.First(l => string.Equals(l, line.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>"" for blank / N/A, an active team's exact name, or null (with error) otherwise.</summary>
        private string? ResolveTeam(string? team, out string? error)
        {
            error = null;
            var t = Clean(team);
            if (t == "" || t.Equals("N/A", StringComparison.OrdinalIgnoreCase)) return "";
            if (_db.Teams.AsNoTracking().Any(x => x.IsActive && x.Name == t)) return t;
            error = $"Team '{t}' isn't an active Team.";
            return null;
        }

        /// <summary>
        /// "Sample 1a" for a Sample # of "1a". The field's own placeholder is
        /// "e.g. Sample 1a", so people type the word too; don't print it twice.
        /// </summary>
        private static string SampleLabel(string sample) =>
            sample.StartsWith("sample", StringComparison.OrdinalIgnoreCase) ? sample : "Sample " + sample;

        private static string Place(string line)
        {
            var branch = OrgStructure.BranchFor(line);
            return branch == null ? line : $"{branch} › {line}";
        }

        private void Log(string actionType, int quantityChange, string details)
        {
            // Blank ItemId is VIS's "not about an inventory item" marker. A tag
            // doesn't exist yet, and a non-blank value that isn't an item would be
            // read as a deleted item (handoff Pass 14).
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = actionType,
                ItemId = "",
                ItemName = null,
                QuantityChange = quantityChange,
                Details = details,
                User = _user.Name
            });
        }

        /// <summary>
        /// False when the row vanished between the load and the write (EF sees an
        /// UPDATE/DELETE hit zero rows). Nothing was changed in that case, so the
        /// caller reports "no longer exists" instead of letting the exception out.
        /// </summary>
        private bool TrySave()
        {
            try
            {
                _db.SaveChanges();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        /// <summary>
        /// " Heads up: ..." text for the success toast, or "". Warns, never blocks
        /// (blocking is an open question for Conner). Compared AFTER materializing:
        /// EF Core can't translate string.Equals(a, b, StringComparison...) against
        /// a live query (handoff Pass 29 trap).
        /// </summary>
        private string DuplicateWarnings(TrackedUnit unit)
        {
            var others = _db.TrackedUnits.AsNoTracking()
                .Where(u => u.Id != unit.Id)
                .Select(u => new { u.Id, u.ModelNumber, u.SerialNumber, u.SampleNumber })
                .ToList();

            var warnings = new List<string>();

            if (unit.SerialNumber != "")
            {
                var same = others.Where(o => o.SerialNumber != ""
                    && string.Equals(o.SerialNumber, unit.SerialNumber, StringComparison.OrdinalIgnoreCase))
                    .Select(o => "#" + o.Id).ToList();
                if (same.Count > 0)
                    warnings.Add($"Heads up: serial {unit.SerialNumber} is also on {string.Join(", ", same)}.");
            }
            else if (unit.SampleNumber != "")
            {
                // No serial to go on: the same model + Sample # on a row that also
                // has no serial is probably the same physical unit entered twice.
                var same = others.Where(o => o.SerialNumber == "" && o.SampleNumber != ""
                    && string.Equals(o.ModelNumber, unit.ModelNumber, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(o.SampleNumber, unit.SampleNumber, StringComparison.OrdinalIgnoreCase))
                    .Select(o => "#" + o.Id).ToList();
                if (same.Count > 0)
                    warnings.Add($"Heads up: {unit.ModelNumber} {SampleLabel(unit.SampleNumber)} is also on {string.Join(", ", same)}.");
            }

            return warnings.Count == 0 ? "" : " " + string.Join(" ", warnings);
        }
    }
}
