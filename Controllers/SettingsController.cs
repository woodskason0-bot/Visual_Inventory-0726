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
