using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;   // AsNoTracking on the map-zone queries (Pass 8)
using System;
using System.Linq;
using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;
using Visual_Inventory_System.Services;

namespace Visual_Inventory_System.Controllers
{
    /// <summary>
    /// App Settings: the one screen that replaces "open the SQLite file and
    /// edit a row by hand." Gated by [RequireSuperuser] -- session name must
    /// match Superuser:UserName AND the session must have entered the
    /// Superuser:Passcode from appsettings.json. Every change here writes a
    /// TransactionLog row, same convention as Edit Details.
    /// </summary>
    [RequireSuperuser]
    public class SettingsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly SuperuserGateService _gate;
        private readonly CurrentUserService _currentUser;
        // Pass 9: approving a held intake runs the SAME commit path the Intake
        // screen uses, rather than a second copy of the logic living in here.
        private readonly InventoryService _inventoryService;

        public SettingsController(AppDbContext db, SuperuserGateService gate,
            CurrentUserService currentUser, InventoryService inventoryService)
        {
            _db = db;
            _gate = gate;
            _currentUser = currentUser;
            _inventoryService = inventoryService;
        }

        [HttpGet]
        [AllowWithoutSuperuser]
        public IActionResult Unlock()
        {
            if (_gate.IsUnlocked) return RedirectToAction("Index");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [AllowWithoutSuperuser]
        public IActionResult Unlock(string passcode)
        {
            if (_gate.TryUnlock(passcode ?? ""))
                return RedirectToAction("Index");

            TempData["AuthError"] = "Wrong passcode.";
            return RedirectToAction("Unlock");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Lock()
        {
            _gate.Lock();
            TempData["Success"] = "Settings locked.";
            return RedirectToAction("Index", "Home");
        }

        public IActionResult Index()
        {
            ViewBag.Teams = _db.Teams.OrderByDescending(t => t.IsActive).ThenBy(t => t.Name).ToList();
            // Pass 9: intakes held because their location wasn't recognised.
            ViewBag.PendingIntakes = _db.IntakeBatches.Include(b => b.Rows)
                .Where(b => b.Status == IntakeStatus.Pending)
                .OrderBy(b => b.SubmittedAt).ToList();
            ViewBag.Locations = _db.Locations.OrderBy(l => l.Level).ThenBy(l => l.Name).ToList();
            // Pass 8: existing map rectangles, so the editor can draw what's already
            // placed and you can see coverage before adding more.
            ViewBag.MapZones = _db.LocationZones.AsNoTracking()
                .Join(_db.Locations, z => z.LocationId, l => l.Id,
                      (z, l) => new { z.Id, z.LocationId, l.Name, z.X, z.Y, z.W, z.H, z.Label })
                .OrderBy(z => z.Name).ToList();
            // Codes shown next to each name are DERIVED, never stored -- so what the
            // panel displays is exactly what Encode() will produce.
            ViewBag.LocationCodes = _db.Locations.ToList()
                .ToDictionary(l => l.Id, l => LocationCodec.Encode(l.Name));
            // How many items still point at each team name -- shown next to Hide so
            // nobody hides a team without seeing what references it.
            ViewBag.TeamUsage = _db.InventoryItems
                .Where(i => i.Team != null && i.Team != "")
                .GroupBy(i => i.Team)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Name, x => x.Count);
            ViewBag.Users = _db.Users.OrderBy(u => u.DisplayName).ToList();
            ViewBag.PickupSubscriptions = _db.NotificationSubscriptions
                .Where(s => s.Category == "PickupRequested")
                .ToDictionary(s => s.UserId, s => s.Enabled);
            ViewBag.OrgStructureJson = System.Text.Json.JsonSerializer.Serialize(OrgStructure.BranchLines);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateAccessLevel(int userId, int newLevel)
        {
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Index");
            }

            if (newLevel < AccessLevels.Viewer || newLevel > AccessLevels.Admin)
            {
                TempData["Error"] = "Invalid access level.";
                return RedirectToAction("Index");
            }

            int oldLevel = user.AccessLevel;
            if (oldLevel == newLevel)
            {
                TempData["Success"] = $"{user.DisplayName} is already {AccessLevels.Name(newLevel)}.";
                return RedirectToAction("Index");
            }

            user.AccessLevel = newLevel;

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Access Level Changed",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{user.DisplayName} ({user.UserName}): {AccessLevels.Name(oldLevel)} -> {AccessLevels.Name(newLevel)}",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            TempData["Success"] = $"{user.DisplayName} is now {AccessLevels.Name(newLevel)}.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleActive(int userId)
        {
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Index");
            }

            user.IsActive = !user.IsActive;

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "User Active Toggled",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{user.DisplayName}: IsActive -> {user.IsActive}",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            TempData["Success"] = $"{user.DisplayName} is now {(user.IsActive ? "active" : "hidden")}.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateLine(int userId, string line)
        {
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Index");
            }

            line = (line ?? "").Trim();
            if (line.Length > 0 && !OrgStructure.IsValidLine(line))
            {
                TempData["Error"] = $"'{line}' isn't a recognized Line.";
                return RedirectToAction("Index");
            }

            string oldLine = user.Line ?? "";
            user.Line = line.Length > 0 ? line : null;

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Line Changed",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{user.DisplayName}: '{(oldLine.Length > 0 ? oldLine : "unassigned")}' -> '{(line.Length > 0 ? line : "unassigned")}'",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            TempData["Success"] = $"{user.DisplayName}'s Line is now {(line.Length > 0 ? line : "unassigned")}.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddUser(string displayName, string? team, int accessLevel, string? line)
        {
            displayName = (displayName ?? "").Trim();
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                TempData["Error"] = "Enter a full name (First Last) to add a user.";
                return RedirectToAction("Index");
            }

            if (accessLevel < AccessLevels.Viewer || accessLevel > AccessLevels.Admin)
            {
                TempData["Error"] = "Invalid access level.";
                return RedirectToAction("Index");
            }

            line = (line ?? "").Trim();
            if (line.Length > 0 && !OrgStructure.IsValidLine(line))
            {
                TempData["Error"] = $"'{line}' isn't a recognized Line.";
                return RedirectToAction("Index");
            }

            // Same First.Last convention as the Program.cs seed list and Identify.
            string userName = parts[0] + "." + parts[parts.Length - 1];
            string normalizedDisplay = string.Join(" ", parts);

            var existing = _db.Users.FirstOrDefault(u => u.UserName.ToLower() == userName.ToLower());
            if (existing != null)
            {
                TempData["Error"] = $"'{userName}' already exists ({existing.DisplayName}).";
                return RedirectToAction("Index");
            }

            var newUser = new User
            {
                DisplayName = normalizedDisplay,
                UserName = userName,
                Team = string.IsNullOrWhiteSpace(team) ? null : team.Trim(),
                Line = line.Length > 0 ? line : null,
                Theme = "dark",
                IsActive = true,
                AccessLevel = accessLevel
            };
            _db.Users.Add(newUser);
            _db.SaveChanges(); // need newUser.Id before adding a subscription row

            // Matches today's implicit behavior: Standard tier used to mean pickup
            // alerts came along for free. Keep that true for anyone added at Standard.
            if (accessLevel == AccessLevels.Standard)
            {
                _db.NotificationSubscriptions.Add(new NotificationSubscription
                {
                    UserId = newUser.Id,
                    Category = "PickupRequested",
                    Enabled = true
                });
            }

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "User Added",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{newUser.DisplayName} ({newUser.UserName}) added as {AccessLevels.Name(accessLevel)}.",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            TempData["Success"] = $"{newUser.DisplayName} added.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteUser(int userId, string confirmName)
        {
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Index");
            }

            if (!string.Equals((confirmName ?? "").Trim(), user.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Name didn't match -- nothing was deleted.";
                return RedirectToAction("Index");
            }

            string display = user.DisplayName;
            string login = user.UserName;

            // Hard delete. Safe: TransactionLog/Order/VisTask snapshot the actor's
            // name as plain text (not a foreign key), so history is unaffected.
            // NotificationSubscriptions for this user cascade-delete with the row.
            _db.Users.Remove(user);

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "User Removed",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{display} ({login}) removed from the roster.",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            TempData["Success"] = $"{display} removed.";
            return RedirectToAction("Index");
        }


        // ==========================================================
        // TEAMS (Pass 7A)
        // ==========================================================
        // Replaces four hardcoded <option> lists and the ternary
        //   ProjectCode = newTeam == "ninja" ? "7165" : "7166"
        // which quietly gave every team that wasn't Ninja Samurai's code.
        //
        // RENAME IS DELIBERATELY NOT OFFERED. Items store the team NAME as a
        // plain string (same as Line), so a rename would orphan every item
        // pointing at the old value unless it cascaded -- and a cascading
        // rename is hard to undo. Add a new team and hide the old one instead;
        // that leaves the history readable either way.


        // ==========================================================
        // LOCATIONS (Pass 7C)
        // ==========================================================
        // The vocabulary used to live in four hand-kept copies -- the codec's const
        // array, the JS cascade map, and two hardcoded <option> lists. They drifted:
        // 34 variant rows carried Sub codes no copy knew, so breadcrumbs showed raw
        // codes. One table now feeds all four.
        //
        // Codes are NEVER stored. LocationCodec.Encode() derives them from the name
        // by the same 1st/3rd/5th/last rule, so nothing here can contradict it.

        private void RefreshLocationCodec()
        {
            LocationCodec.Refresh(_db.Locations.Where(l => l.IsActive).Select(l => l.Name).ToList());
        }



        // ==========================================================
        // PENDING INTAKES (Pass 9)
        // ==========================================================
        // A batch lands here only because the location it named isn't recognised.
        // Approving takes one of two shapes -- MAP it onto somewhere that already
        // exists under a different name, or CREATE the location -- and then runs
        // the identical commit path a known-location batch would have taken.

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ApproveIntake(int batchId, int? mapToLocationId,
            string? newName, string? newLevel, int? newOwnerId, string? rack, string? row)
        {
            var batch = _db.IntakeBatches.Include(b => b.Rows).FirstOrDefault(b => b.Id == batchId);
            if (batch == null || batch.Status != IntakeStatus.Pending)
            {
                TempData["Error"] = "That batch is no longer pending.";
                return RedirectToAction("Index");
            }

            Location? target;
            string how;

            if (mapToLocationId.HasValue && mapToLocationId.Value > 0)
            {
                target = _db.Locations.FirstOrDefault(l => l.Id == mapToLocationId.Value);
                if (target == null) { TempData["Error"] = "That location no longer exists."; return RedirectToAction("Index"); }
                how = $"mapped '{batch.RequestedLocation}' to existing {target.Level} '{target.Name}'";
            }
            else
            {
                newName = (newName ?? "").Trim();
                newLevel = (newLevel ?? "").Trim();
                if (newName.Length == 0 || !LocationLevel.All.Contains(newLevel))
                {
                    TempData["Error"] = "Either map it to an existing location, or give a name and level for a new one.";
                    return RedirectToAction("Index");
                }
                if (newLevel != LocationLevel.Parent && (newOwnerId == null || newOwnerId <= 0))
                {
                    TempData["Error"] = $"A {newLevel} needs an owner one level up.";
                    return RedirectToAction("Index");
                }
                var clash = CodeClashMessage(newName);
                if (clash != null) { TempData["Error"] = clash; return RedirectToAction("Index"); }

                target = new Location
                {
                    Name = newName, Level = newLevel,
                    ParentId = newLevel == LocationLevel.Parent ? null : newOwnerId,
                    IsActive = true
                };
                _db.Locations.Add(target);
                _db.SaveChanges();
                RefreshLocationCodec();
                how = $"created {newLevel} '{newName}' for requested '{batch.RequestedLocation}'";
            }

            // Walk up from the resolved location so the batch lands at the right
            // depth whichever level was chosen -- mapping a request onto a Sub has
            // to fill in its Major and Parent too, or the item files itself at the
            // wrong level and nobody notices until someone goes looking for it.
            string pCode = "", mCode = "", sCode = "";
            var chain = new List<Location>();
            var cur = target;
            while (cur != null) { chain.Insert(0, cur); cur = cur.ParentId.HasValue ? _db.Locations.FirstOrDefault(l => l.Id == cur.ParentId.Value) : null; }
            foreach (var l in chain)
            {
                if (l.Level == LocationLevel.Parent) pCode = LocationCodec.Encode(l.Name);
                else if (l.Level == LocationLevel.Major) mCode = LocationCodec.Encode(l.Name);
                else if (l.Level == LocationLevel.Sub) sCode = LocationCodec.Encode(l.Name);
            }
            if (pCode.Length == 0)
            {
                TempData["Error"] = $"'{target.Name}' has no Parent above it — fix its place in the Locations tree first.";
                return RedirectToAction("Index");
            }

            var lines = batch.Rows.Select(r => new InventoryService.IntakeLine
            {
                ItemName = r.ItemName, Type = r.Type, Brand = r.Brand,
                RheemPartNumber = r.RheemPartNumber, Quantity = r.Quantity, SerialNumber = r.SerialNumber
            }).ToList();

            // The submitter's own Line still drives the ItemId prefix, not the
            // approver's -- approving somebody's batch shouldn't rewrite whose
            // record it is.
            string submitterLine = _db.Users.AsNoTracking()
                .FirstOrDefault(u => u.UserName == batch.SubmittedBy)?.Line ?? "";

            var result = _inventoryService.CommitIntake(
                lines, batch.Line, batch.Team,
                pCode, mCode, sCode,
                string.IsNullOrWhiteSpace(rack) ? batch.Rack : rack.Trim(),
                string.IsNullOrWhiteSpace(row) ? batch.Row : row.Trim(),
                batch.SubmittedBy, submitterLine, preview: false);

            if (!result.Ok)
            {
                TempData["Error"] = "Approved the location, but the rows didn't import: " + string.Join(" | ", result.Errors);
                return RedirectToAction("Index");
            }

            batch.Status = IntakeStatus.Approved;
            batch.ResolvedBy = _currentUser.Name;
            batch.ResolvedAt = DateTime.UtcNow;
            batch.Note = how;
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow, ActionType = "Intake Approved", ItemId = "", QuantityChange = result.UnitsIn,
                Details = $"Batch #{batch.Id} from {batch.SubmittedBy}: {how}. {result.NewItems.Count} new, {result.AddedToExisting.Count} added, {result.UnitsIn} unit(s).",
                User = _currentUser.Name
            });
            _db.SaveChanges();
            TempData["Success"] = $"Batch #{batch.Id} imported — {how}. {result.UnitsIn} unit(s) in.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RejectIntake(int batchId, string? note)
        {
            var batch = _db.IntakeBatches.FirstOrDefault(b => b.Id == batchId);
            if (batch == null) { TempData["Error"] = "That batch no longer exists."; return RedirectToAction("Index"); }
            batch.Status = IntakeStatus.Rejected;
            batch.ResolvedBy = _currentUser.Name;
            batch.ResolvedAt = DateTime.UtcNow;
            batch.Note = (note ?? "").Trim();
            // Rows are KEPT. A rejected batch is still a record of what somebody
            // counted, and deleting it means they have to type it all again if the
            // rejection turns out to be a misunderstanding.
            _db.SaveChanges();
            TempData["Success"] = $"Batch #{batch.Id} rejected. The rows are kept in case it needs revisiting.";
            return RedirectToAction("Index");
        }

        // ==========================================================
        // MAP ZONES (Pass 8)
        // ==========================================================
        // Coordinates arrive as FRACTIONS of the image (0-1) from the drag editor,
        // never pixels. That is what lets the same rectangle survive the floorplan
        // being re-exported at a different size.
        //
        // Only Parent locations get zones: Majors and Subs are reached through the
        // post-click menu, which is already driven by live stock data.


        // A name whose derived code collides with an existing one makes Decode()
        // ambiguous -- first writer wins and the second becomes invisible on every
        // breadcrumb. Shared by AddLocation and the inline new-Parent path in
        // AddZone so both refuse it the same way.
        private string? CodeClashMessage(string name)
        {
            string code = LocationCodec.Encode(name);
            var clash = _db.Locations.Where(l => l.IsActive).ToList()
                .FirstOrDefault(l => LocationCodec.Encode(l.Name) == code);
            return clash == null
                ? null
                : $"'{name}' encodes to {code}, which '{clash.Name}' already uses. Pick a name that encodes differently.";
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddZone(int locationId, double x, double y, double w, double h, string? label, string? newParentName)
        {
            // Geometry is checked BEFORE anything is created, so a bad drag can't
            // leave a brand-new Parent behind with no zone attached to it.
            if (w <= 0.001 || h <= 0.001)
            {
                TempData["Error"] = "That box is too small to click. Drag a larger area.";
                return RedirectToAction("Index");
            }
            if (x < 0 || y < 0 || x + w > 1.0001 || y + h > 1.0001)
            {
                TempData["Error"] = "That box falls outside the map image.";
                return RedirectToAction("Index");
            }

            Location? loc;
            newParentName = (newParentName ?? "").Trim();

            if (newParentName.Length > 0)
            {
                // Inline creation: drawing a box round an area you haven't registered
                // yet is the normal case when a new team turns up, and bouncing to the
                // Locations panel and back for it is friction with no purpose.
                if (_db.Locations.Any(l => l.Level == LocationLevel.Parent && l.Name.ToLower() == newParentName.ToLower()))
                {
                    TempData["Error"] = $"A Parent called '{newParentName}' already exists — pick it from the list instead.";
                    return RedirectToAction("Index");
                }
                var clash = CodeClashMessage(newParentName);
                if (clash != null) { TempData["Error"] = clash; return RedirectToAction("Index"); }

                loc = new Location { Name = newParentName, Level = LocationLevel.Parent, ParentId = null, IsActive = true };
                _db.Locations.Add(loc);
                _db.TransactionLogs.Add(new TransactionLog
                {
                    Timestamp = DateTime.UtcNow,
                    ActionType = "Location Added",
                    ItemId = "",
                    QuantityChange = 0,
                    Details = $"Parent '{newParentName}' added from the map editor (code {LocationCodec.Encode(newParentName)}).",
                    User = _currentUser.Name
                });
                _db.SaveChanges();   // need the Id before the zone can point at it
                RefreshLocationCodec();
            }
            else
            {
                loc = _db.Locations.FirstOrDefault(l => l.Id == locationId);
                if (loc == null) { TempData["Error"] = "That location no longer exists."; return RedirectToAction("Index"); }
            }
            if (loc.Level != LocationLevel.Parent)
            {
                TempData["Error"] = $"Only Parent locations appear on the map. '{loc.Name}' is a {loc.Level}.";
                return RedirectToAction("Index");
            }

            // Overlap is WARNED, not blocked -- a small shared edge between two
            // neighbouring areas is normal. HTML image maps resolve a click to
            // whichever <area> comes first, so a real overlap makes one of them
            // unreachable and you want to know without being stopped.
            var overlaps = _db.LocationZones.AsNoTracking()
                .Join(_db.Locations, z => z.LocationId, l => l.Id, (z, l) => new { z, l.Name })
                .ToList()
                .Where(e => x < e.z.X + e.z.W && x + w > e.z.X && y < e.z.Y + e.z.H && y + h > e.z.Y)
                .Select(e => e.Name).Distinct().ToList();

            _db.LocationZones.Add(new LocationZone
            {
                LocationId = loc.Id,
                X = x, Y = y, W = w, H = h,
                Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim()
            });
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Map Zone Added",
                ItemId = "",
                QuantityChange = 0,
                Details = $"Zone drawn for '{loc.Name}' at {x:F3},{y:F3} size {w:F3}x{h:F3}"
                          + (overlaps.Count > 0 ? $" -- overlaps {string.Join(", ", overlaps)}." : "."),
                User = _currentUser.Name
            });
            _db.SaveChanges();

            string created = newParentName.Length > 0 ? $"Parent '{loc.Name}' created and zone drawn" : $"Zone added for '{loc.Name}'";
            TempData["Success"] = overlaps.Count > 0
                ? $"{created}, but it overlaps {string.Join(", ", overlaps)} — the topmost one wins a click."
                : $"{created}.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteZone(int zoneId)
        {
            var zone = _db.LocationZones.FirstOrDefault(z => z.Id == zoneId);
            if (zone == null) { TempData["Error"] = "That zone no longer exists."; return RedirectToAction("Index"); }
            var loc = _db.Locations.FirstOrDefault(l => l.Id == zone.LocationId);

            _db.LocationZones.Remove(zone);
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Map Zone Removed",
                ItemId = "",
                QuantityChange = 0,
                Details = $"Zone removed from '{loc?.Name ?? "(unknown)"}'. The location and its stock are untouched.",
                User = _currentUser.Name
            });
            _db.SaveChanges();
            TempData["Success"] = $"Zone removed. '{loc?.Name}' is still in every picker — it just isn't on the map.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddLocation(string name, string level, int? parentId)
        {
            name = (name ?? "").Trim();
            level = (level ?? "").Trim();

            if (name.Length == 0) { TempData["Error"] = "A location needs a name."; return RedirectToAction("Index"); }
            if (!LocationLevel.All.Contains(level)) { TempData["Error"] = "Pick Parent, Major or Sub."; return RedirectToAction("Index"); }
            if (level != LocationLevel.Parent && (parentId == null || parentId <= 0))
            {
                TempData["Error"] = $"A {level} needs an owner one level up.";
                return RedirectToAction("Index");
            }
            if (level == LocationLevel.Parent) parentId = null;

            if (_db.Locations.Any(l => l.Level == level && l.ParentId == parentId && l.Name.ToLower() == name.ToLower()))
            {
                TempData["Error"] = $"'{name}' already exists at that level.";
                return RedirectToAction("Index");
            }

            string code = LocationCodec.Encode(name);
            var clashMsg = CodeClashMessage(name);
            if (clashMsg != null) { TempData["Error"] = clashMsg; return RedirectToAction("Index"); }

            _db.Locations.Add(new Location { Name = name, Level = level, ParentId = parentId, IsActive = true });
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Location Added",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{level} '{name}' added (code {code}).",
                User = _currentUser.Name
            });
            _db.SaveChanges();
            RefreshLocationCodec();
            TempData["Success"] = $"{level} '{name}' added — code {code}.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleLocationActive(int locationId)
        {
            var loc = _db.Locations.FirstOrDefault(l => l.Id == locationId);
            if (loc == null) { TempData["Error"] = "That location no longer exists."; return RedirectToAction("Index"); }

            loc.IsActive = !loc.IsActive;
            string code = LocationCodec.Encode(loc.Name);
            int inUse = _db.ItemVariants.Count(v => v.Parent == code || v.Major == code || v.Sub == code);

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Location Updated",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{loc.Level} '{loc.Name}' ({code}): IsActive -> {loc.IsActive}. {inUse} variant(s) reference it.",
                User = _currentUser.Name
            });
            _db.SaveChanges();
            RefreshLocationCodec();

            // Hiding also drops the name from the decode map, so anything still
            // stored at that code goes back to rendering raw. Say so plainly.
            TempData["Success"] = loc.IsActive
                ? $"'{loc.Name}' is selectable again."
                : $"'{loc.Name}' hidden. {inUse} variant(s) still stored there will show as '{code}' until moved.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AddTeam(string name, string? projectCode)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                TempData["Error"] = "A team needs a name.";
                return RedirectToAction("Index");
            }
            if (string.Equals(name, "N/A", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "\"N/A\" is the built-in blank option -- pick a different name.";
                return RedirectToAction("Index");
            }
            if (_db.Teams.Any(t => t.Name.ToLower() == name.ToLower()))
            {
                TempData["Error"] = $"A team called '{name}' already exists.";
                return RedirectToAction("Index");
            }

            _db.Teams.Add(new Team
            {
                Name = name,
                ProjectCode = (projectCode ?? "").Trim(),
                IsActive = true
            });
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Team Added",
                ItemId = "",
                QuantityChange = 0,
                Details = $"Team '{name}' added" + (string.IsNullOrWhiteSpace(projectCode) ? " (no project code)" : $" with project code {projectCode.Trim()}") + ".",
                User = _currentUser.Name
            });
            _db.SaveChanges();
            TempData["Success"] = $"Team '{name}' added.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateProjectCode(int teamId, string? projectCode)
        {
            var team = _db.Teams.FirstOrDefault(t => t.Id == teamId);
            if (team == null)
            {
                TempData["Error"] = "That team no longer exists.";
                return RedirectToAction("Index");
            }
            string old = team.ProjectCode ?? "";
            string now = (projectCode ?? "").Trim();
            if (old == now) return RedirectToAction("Index");

            team.ProjectCode = now;
            // Project code is display/reporting only -- nothing keys off it, so
            // this does NOT touch the items already carrying the old value.
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Team Updated",
                ItemId = "",
                QuantityChange = 0,
                Details = $"Team '{team.Name}' project code '{(old.Length == 0 ? "(blank)" : old)}' -> '{(now.Length == 0 ? "(blank)" : now)}'. Existing items keep their recorded code.",
                User = _currentUser.Name
            });
            _db.SaveChanges();
            TempData["Success"] = $"Project code updated for '{team.Name}'.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleTeamActive(int teamId)
        {
            var team = _db.Teams.FirstOrDefault(t => t.Id == teamId);
            if (team == null)
            {
                TempData["Error"] = "That team no longer exists.";
                return RedirectToAction("Index");
            }
            team.IsActive = !team.IsActive;

            int inUse = _db.InventoryItems.Count(i => i.Team == team.Name);
            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Team Updated",
                ItemId = "",
                QuantityChange = 0,
                Details = $"Team '{team.Name}': IsActive -> {team.IsActive}. {inUse} item(s) still reference it and are unaffected.",
                User = _currentUser.Name
            });
            _db.SaveChanges();
            TempData["Success"] = team.IsActive
                ? $"'{team.Name}' is selectable again."
                : $"'{team.Name}' hidden from the pickers. {inUse} existing item(s) keep it.";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateNotificationSubscription(int userId, string category, bool enabled)
        {
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction("Index");
            }

            var sub = _db.NotificationSubscriptions.FirstOrDefault(s => s.UserId == userId && s.Category == category);
            if (sub == null)
            {
                sub = new NotificationSubscription { UserId = userId, Category = category };
                _db.NotificationSubscriptions.Add(sub);
            }
            sub.Enabled = enabled;

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "Notification Subscription Changed",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{user.DisplayName}: {category} -> {(enabled ? "On" : "Off")}",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            TempData["Success"] = $"{user.DisplayName}'s {category} alerts are now {(enabled ? "on" : "off")}.";
            return RedirectToAction("Index");
        }    }
}
