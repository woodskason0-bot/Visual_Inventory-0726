using Microsoft.AspNetCore.Mvc;
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

        public SettingsController(AppDbContext db, SuperuserGateService gate, CurrentUserService currentUser)
        {
            _db = db;
            _gate = gate;
            _currentUser = currentUser;
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
