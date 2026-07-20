using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Creates and serves in-app notifications. One row per recipient (fan-out on
    /// write), so dismissing is per-person and needs no join table. Event-based only --
    /// the live low-stock summary for managers is computed from inventory at render
    /// time, not stored here.
    /// </summary>
    public class NotificationService
    {
        private readonly AppDbContext _db;

        public NotificationService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>Notify one person by their First.Last user name.</summary>
        public void Create(string recipientUserName, string category, string message, string? linkUrl = null)
        {
            if (string.IsNullOrWhiteSpace(recipientUserName)) return;

            _db.Notifications.Add(new Notification
            {
                RecipientUserName = recipientUserName.Trim(),
                Category = category,
                Message = message,
                LinkUrl = linkUrl,
                CreatedAt = DateTime.UtcNow
            });
            _db.SaveChanges();
        }

        /// <summary>
        /// Fan the same notification out to every active user in an access-level band.
        /// Pass maxLevel = null for "this level and up" (e.g. minLevel 2 = runners and
        /// above). Skips excludeUserName so the actor never notifies themself.
        /// </summary>
        public void CreateForLevel(int minLevel, int? maxLevel, string category, string message,
                                   string? linkUrl = null, string? excludeUserName = null)
        {
            var recipients = _db.Users
                .Where(u => u.IsActive
                    && u.AccessLevel >= minLevel
                    && (maxLevel == null || u.AccessLevel <= maxLevel))
                .Select(u => u.UserName)
                .ToList();

            var now = DateTime.UtcNow;
            foreach (var name in recipients)
            {
                if (!string.IsNullOrWhiteSpace(excludeUserName)
                    && string.Equals(name, excludeUserName, StringComparison.OrdinalIgnoreCase))
                    continue;

                _db.Notifications.Add(new Notification
                {
                    RecipientUserName = name,
                    Category = category,
                    Message = message,
                    LinkUrl = linkUrl,
                    CreatedAt = now
                });
            }
            _db.SaveChanges();
        }

        /// <summary>Active (not dismissed) notifications for a person, newest first.</summary>
        public List<Notification> GetActiveFor(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return new List<Notification>();

            return _db.Notifications
                .Where(n => n.RecipientUserName == userName && !n.IsDismissed)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();
        }

        /// <summary>Count of unread, not-yet-dismissed notifications (for the bell badge).</summary>
        public int UnreadCountFor(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return 0;

            return _db.Notifications
                .Count(n => n.RecipientUserName == userName && !n.IsRead && !n.IsDismissed);
        }

        /// <summary>Mark everything currently visible to the person as read (called when the bell opens).</summary>
        public void MarkAllRead(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) return;

            var unread = _db.Notifications
                .Where(n => n.RecipientUserName == userName && !n.IsRead && !n.IsDismissed)
                .ToList();

            if (unread.Count == 0) return;
            foreach (var n in unread) n.IsRead = true;
            _db.SaveChanges();
        }

        /// <summary>Dismiss (x) one notification, scoped to its owner so you cannot clear someone else's.</summary>
        public void Dismiss(int id, string userName)
        {
            var n = _db.Notifications
                .FirstOrDefault(x => x.Id == id && x.RecipientUserName == userName);
            if (n == null) return;

            n.IsDismissed = true;
            _db.SaveChanges();
        }
    }
}
