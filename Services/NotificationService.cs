using Visual_Inventory_System.Data;
using Visual_Inventory_System.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// In-app notifications. One row per recipient, so dismissing is per person.
    /// Event-based only: the low-stock summary is computed at render time, not stored.
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

        /// <summary>Every active user in a level band. maxLevel null = this level and up. Skips excludeUserName.</summary>
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

        /// <summary>
        /// Every active user at one level whose Line is in `lines`. Skips everyone in `exclude`.
        /// Returns how many were written.
        /// </summary>
        public int CreateForLine(IEnumerable<string> lines, int level, string category,
                                 string message, string? linkUrl = null, params string[] exclude)
        {
            var wanted = (lines ?? Enumerable.Empty<string>())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim().ToLower())
                .Distinct()
                .ToList();
            if (wanted.Count == 0) return 0;

            var skip = new HashSet<string>(
                (exclude ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            // ToLower(), not the StringComparison overload: EF cannot translate that one.
            var recipients = _db.Users
                .Where(u => u.IsActive
                    && u.AccessLevel == level
                    && u.Line != null
                    && wanted.Contains(u.Line.ToLower()))
                .Select(u => u.UserName)
                .ToList();

            var now = DateTime.UtcNow;
            int written = 0;
            foreach (var name in recipients)
            {
                if (skip.Contains(name)) continue;
                _db.Notifications.Add(new Notification
                {
                    RecipientUserName = name,
                    Category = category,
                    Message = message,
                    LinkUrl = linkUrl,
                    CreatedAt = now
                });
                written++;
            }
            if (written > 0) _db.SaveChanges();
            return written;
        }

        /// <summary>Everyone subscribed to this category, whatever their level. Use for personal opt-ins.</summary>
        public void CreateForSubscribers(string category, string message, string? linkUrl = null, string? excludeUserName = null)
        {
            var recipients = _db.Users
                .Where(u => u.IsActive)
                .Join(_db.NotificationSubscriptions.Where(s => s.Category == category && s.Enabled),
                      u => u.Id, s => s.UserId, (u, s) => u.UserName)
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
