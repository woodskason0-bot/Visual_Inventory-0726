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
            ViewBag.Users = _db.Users.OrderBy(u => u.DisplayName).ToList();
            ViewBag.AppSettings = _db.AppSettings.OrderBy(s => s.Key).ToList();
            ViewBag.PickupSubscriptions = _db.NotificationSubscriptions
                .Where(s => s.Category == "PickupRequested")
                .ToDictionary(s => s.UserId, s => s.Enabled);
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
        public IActionResult AddUser(string displayName, string? team, int accessLevel)
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
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateSetting(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                TempData["Error"] = "Setting needs a key.";
                return RedirectToAction("Index");
            }

            var setting = _db.AppSettings.FirstOrDefault(s => s.Key == key.Trim());
            if (setting == null)
            {
                setting = new AppSetting { Key = key.Trim() };
                _db.AppSettings.Add(setting);
            }

            setting.Value = (value ?? "").Trim();
            setting.UpdatedAt = DateTime.UtcNow;
            setting.UpdatedBy = _currentUser.Name;

            _db.TransactionLogs.Add(new TransactionLog
            {
                Timestamp = DateTime.UtcNow,
                ActionType = "App Setting Changed",
                ItemId = "",
                QuantityChange = 0,
                Details = $"{setting.Key} -> '{setting.Value}'",
                User = _currentUser.Name
            });

            _db.SaveChanges();
            TempData["Success"] = $"'{key}' updated.";
            return RedirectToAction("Index");
        }
    }
}
