using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Result of a Unit Tracker write. Message is the toast text either way.
    /// Unit is set on a successful Add or Update.
    /// </summary>
    public class TrackedUnitResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public TrackedUnit? Unit { get; init; }
    }

    /// <summary>What the Edit form showed when it was opened. A null field isn't compared.</summary>
    public class UnitSnapshot
    {
        public string? Model { get; init; }
        public string? Package { get; init; }
        public string? Serial { get; init; }
        public string? Sample { get; init; }
        public string? Line { get; init; }
        public string? Team { get; init; }
        public string? Notes { get; init; }
    }

    /// <summary>
    /// Every query and write for the Unit Tracker list. The controller holds no LINQ.
    /// Access rules live here or in [RequireLevel]. The server is the only validation
    /// that counts: over-length values are refused, never truncated.
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

        // Reads

        /// <summary>The list, newest first. No Line scoping: every signed-in user sees every row.</summary>
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

            // "none" = nothing recorded, a label = just those, anything else = Any.
            var pkgFilter = (pkg ?? "").Trim();
            if (string.Equals(pkgFilter, TrackedUnitPackage.NotSet, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(u => u.PackageType == "");
            }
            else
            {
                var canonicalPkg = TrackedUnitPackage.Canonical(pkgFilter);
                if (canonicalPkg != null) query = query.Where(u => u.PackageType == canonicalPkg);
            }

            var teamFilter = (team ?? "").Trim();
            if (teamFilter != "") query = query.Where(u => u.Team == teamFilter);

            var lineFilter = (line ?? "").Trim();
            if (lineFilter != "") query = query.Where(u => u.Line == lineFilter);

            // Same three-way rule.
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

            // Same three-way rule.
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

        /// <summary>Active teams by name, for the Add/Edit Team picker.</summary>
        public List<Team> ActiveTeams() =>
            _db.Teams.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToList();

        /// <summary>Active team name -> home Line, for the Team-picks-Line autofill. Teams with no Line are absent.</summary>
        public Dictionary<string, string?> TeamLines() =>
            _db.Teams.AsNoTracking().Where(t => t.IsActive && t.Line != null)
                .ToDictionary(t => t.Name, t => t.Line);

        // Writes

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

            // Optional: blank (or not posted) is stored as "" = not recorded.
            var pkg = CanonicalPackage(packageType);
            if (pkg == null) return Fail(PackageRefusal);

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

            // The log names the unit's Id, which the INSERT assigns, so the two saves
            // share one transaction: both rows land or neither does.
            using var tx = _db.Database.BeginTransaction();
            _db.TrackedUnits.Add(unit);
            _db.SaveChanges();
            Log("Tracked Unit Added", +1,
                $"Unit #{unit.Id} · {model}"
                + (pkg != "" ? $" · {pkg}" : "")
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
                Message = $"Unit #{unit.Id} added ({ModelAndPackage(model, pkg)})." + DuplicateWarnings(unit)
            };
        }

        /// <summary>
        /// Standard and up. A null line or team was not posted and is left alone.
        /// A changed Line or Team is a move: Engineer only, and validated only when it changed.
        /// </summary>
        public TrackedUnitResult Update(int id, string? modelNumber, string? packageType, string? serialNumber,
            string? sampleNumber, string? line, string? team, string? notes, UnitSnapshot? opened = null)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);
            var stale = ChangedSince(unit, opened);
            if (stale != null) return Fail(stale + StaleRefusal);

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

            // Null = not posted, left alone. A posted blank ("Not set") clears it.
            var pkg = packageType == null ? unit.PackageType : CanonicalPackage(packageType);
            if (pkg == null) return Fail(PackageRefusal);

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

            // Table-column order. Notes text is never logged.
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
                Message = $"Unit #{unit.Id} updated ({ModelAndPackage(unit.ModelNumber, unit.PackageType)})." + DuplicateWarnings(unit)
            };
        }

        /// <summary>Anyone signed in, Viewer included. Changes the Notes and nothing else.</summary>
        public TrackedUnitResult UpdateNotes(int id, string? notes, UnitSnapshot? opened = null)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);
            var stale = ChangedSince(unit, opened);
            if (stale != null) return Fail(stale + StaleRefusal);

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
        /// Standard and up. Sets or clears Whereabouts and nothing else. The same value
        /// again changes nothing and logs nothing. The message carries no typed text.
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

        /// <summary>Standard and up. Sets or clears BuildStatus and nothing else. Works like SetWhereabouts.</summary>
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

        /// <summary>Engineer and up. A hard delete: the log row written here is the record.</summary>
        public TrackedUnitResult Delete(int id)
        {
            var unit = _db.TrackedUnits.Find(id);
            if (unit == null) return Gone(id);

            Log("Tracked Unit Deleted", -1,
                $"Unit #{unit.Id} deleted · {unit.ModelNumber}"
                + (unit.PackageType != "" ? $" · {unit.PackageType}" : "")
                + (unit.SerialNumber != "" ? $" · Serial {unit.SerialNumber}" : "")
                + (unit.SampleNumber != "" ? $" · {SampleLabel(unit.SampleNumber)}" : "")
                + $" · {unit.Line}"
                + (unit.Team != "" ? $" · {unit.Team}" : ""));
            _db.TrackedUnits.Remove(unit);

            if (!TrySave()) return Gone(id);
            return new TrackedUnitResult
            {
                Ok = true,
                Message = $"Unit #{unit.Id} deleted ({ModelAndPackage(unit.ModelNumber, unit.PackageType)})."
            };
        }

        /// <summary>The Edit form's check-in: a sentence if the unit changed since the form opened, else null.</summary>
        public string? CheckChanged(int id, UnitSnapshot? opened)
        {
            var unit = _db.TrackedUnits.AsNoTracking().FirstOrDefault(u => u.Id == id);
            if (unit == null) return $"Unit #{id} no longer exists.";
            var stale = ChangedSince(unit, opened);
            return stale == null ? null : stale + " Reload before you save.";
        }

        // Helpers

        /// <summary>
        /// A sentence when any field the form showed differs from what's stored now, else null.
        /// Stale forms are refused, not merged: someone else's newer value is never overwritten.
        /// </summary>
        private static string? ChangedSince(TrackedUnit unit, UnitSnapshot? opened)
        {
            if (opened == null) return null;
            bool Differs(string? was, string now) => was != null && !string.Equals(Clean(was), now, StringComparison.Ordinal);
            bool changed = Differs(opened.Model, unit.ModelNumber) || Differs(opened.Package, unit.PackageType)
                || Differs(opened.Serial, unit.SerialNumber) || Differs(opened.Sample, unit.SampleNumber)
                || Differs(opened.Line, unit.Line) || Differs(opened.Team, unit.Team) || Differs(opened.Notes, unit.Notes);
            if (!changed) return null;

            string last = "";
            if (unit.UpdatedAt.HasValue)
            {
                var local = unit.UpdatedAt.Value.ToLocalTime();
                string when = local.Date == DateTime.Today ? local.ToString("h:mm tt") : local.ToString("MMM d, h:mm tt");
                last = $" (last saved by {unit.UpdatedBy.Replace('.', ' ')} at {when})";
            }
            return $"Unit #{unit.Id} was changed since you opened it{last}.";
        }

        // The save redirects back to the list, which already shows the newer values.
        private const string StaleRefusal = " Your edit wasn't saved. Open the unit again to see the change, then make yours.";

        private static TrackedUnitResult Fail(string message) => new() { Ok = false, Message = message };

        private static TrackedUnitResult Gone(int id) => Fail($"Unit #{id} no longer exists.");

        /// <summary>Trim, and fold line breaks to "\n" so a break counts once, as the textarea's maxlength counts it.</summary>
        private static string Clean(string? s) =>
            (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        private static string? TooLong(string label, string value, int max) =>
            value.Length > max ? $"{label} can be at most {max} characters (this is {value.Length})." : null;

        private const string PackageRefusal = "Package / Split must be Package, Split or left blank.";

        /// <summary>"" for blank (not recorded), the canonical spelling of Package/Split, or null for anything else.</summary>
        private static string? CanonicalPackage(string? value)
        {
            var v = (value ?? "").Trim();
            return v == "" ? "" : TrackedUnitPackage.Canonical(v);
        }

        /// <summary>"RACG180D, Split", or just "RACG180D" when no Package/Split is recorded.</summary>
        private static string ModelAndPackage(string model, string pkg) =>
            pkg == "" ? model : $"{model}, {pkg}";

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

        /// <summary>"Sample 1a" for "1a". People often type the word too, so it is not printed twice.</summary>
        private static string SampleLabel(string sample) =>
            sample.StartsWith("sample", StringComparison.OrdinalIgnoreCase) ? sample : "Sample " + sample;

        private static string Place(string line)
        {
            var branch = OrgStructure.BranchFor(line);
            return branch == null ? line : $"{branch} › {line}";
        }

        private void Log(string actionType, int quantityChange, string details)
        {
            // Blank ItemId = not about an inventory item. A non-blank non-item would read as a deleted item.
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

        /// <summary>False when the row vanished between load and write. Nothing was changed.</summary>
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
        /// " Heads up: ..." text for the success toast, or "". Warns, never blocks.
        /// Compared after materializing: EF cannot translate the StringComparison overload.
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
                // No serial: the same model + Sample # on another serial-less row is likely a double entry.
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
