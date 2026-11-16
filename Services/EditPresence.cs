using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Who has which Unit Tracker Edit form open. In memory only: a heads-up, not a lock.
    /// A restart forgets it; the stale-save check in TrackedUnitService is the real guard.
    /// </summary>
    public class EditPresence
    {
        // A little over two 45-second check-ins, so one late check-in doesn't drop someone.
        public static readonly TimeSpan Expiry = TimeSpan.FromSeconds(100);

        private readonly ConcurrentDictionary<int, ConcurrentDictionary<string, (DateTime Opened, DateTime Seen)>> _open = new();

        /// <summary>Records this user on the unit and returns everyone else still checking in, with when they opened it.</summary>
        public List<(string User, DateTime Opened)> CheckIn(int unitId, string user, DateTime now)
        {
            // Drop everyone gone quiet, on every unit, so a closed laptop doesn't linger anywhere.
            foreach (var unit in _open)
            {
                foreach (var kv in unit.Value.Where(kv => now - kv.Value.Seen > Expiry).ToList())
                    unit.Value.TryRemove(kv.Key, out _);
                if (unit.Value.IsEmpty) _open.TryRemove(unit.Key, out _);
            }
            var who = _open.GetOrAdd(unitId, _ => new ConcurrentDictionary<string, (DateTime, DateTime)>(StringComparer.OrdinalIgnoreCase));
            who.AddOrUpdate(user, _ => (now, now), (_, v) => (v.Opened, now));
            return who.Where(kv => !string.Equals(kv.Key, user, StringComparison.OrdinalIgnoreCase))
                .Select(kv => (kv.Key, kv.Value.Opened))
                .OrderBy(x => x.Opened)
                .ToList();
        }

        /// <summary>The form closed.</summary>
        public void Leave(int unitId, string user)
        {
            if (_open.TryGetValue(unitId, out var who)) who.TryRemove(user, out _);
        }
    }
}
