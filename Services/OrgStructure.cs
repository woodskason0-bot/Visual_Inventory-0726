using System;
using System.Collections.Generic;
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Org structure: Branches, each holding some Lines. Managed now (Pass 13),
    /// not hardcoded -- Settings can add/soft-hide Branches and Lines via the
    /// Branches/OrgLines tables, same vocabulary-table pattern as Team/Location.
    /// Plain strings on User.Line / InventoryItem.Line / Team.Line (not FK'd to
    /// a table) so ad-hoc SQL against the DB stays simple: UPDATE InventoryItems
    /// SET Line = 'X'.
    ///
    /// This class itself stays static and DB-free, same shape as LocationCodec:
    /// BranchLines is a volatile snapshot, swapped wholesale (never mutated in
    /// place) by Refresh(), which is called once at startup (Program.cs) and
    /// again after any Settings Branch/Line edit
    /// (SettingsController.RefreshOrgStructure()). Every other file in the app
    /// that reads BranchLines/AllLines/BranchFor/GroupFor/IsValidLine is
    /// unaffected by this being DB-backed now -- the public surface didn't change.
    /// </summary>
    public static class OrgStructure
    {
        // Only used until the first real Refresh() call (mirrors LocationCodec's
        // SeedNames) -- matches the Branches/OrgLines migration's own seed data,
        // so a process that somehow renders before that first Refresh still
        // shows something sensible instead of an empty structure.
        private static readonly Dictionary<string, string[]> SeedBranchLines = new()
        {
            ["Residential Air"] = new[] { "Residential OD", "Residential Coils/AH", "Residential Gas Furnaces" },
            ["Commercial Air"] = new[] { "Commercial Packaged/Splits", "Residential Packaged", "International" },
            ["Sustaining"] = new[]
            {
                "944 - International Sustaining - Residential",
                "949 - Nuevo Laredo Sustaining",
                "954 - International Sustaining - Commercial",
                "959 - Fort Smith Sustaining"
            }
        };

        private static volatile Dictionary<string, string[]> _branchLines = SeedBranchLines;

        public static Dictionary<string, string[]> BranchLines => _branchLines;

        public static string[] AllLines => _branchLines.Values.SelectMany(x => x).ToArray();

        /// <summary>
        /// Reloads BranchLines from the DB-managed Branches/OrgLines tables.
        /// Pass only ACTIVE branches with ACTIVE lines -- a soft-hidden Branch or
        /// Line drops out of every picker without touching whatever Line string
        /// items/users already carry (visibility is plain string equality, not a
        /// vocabulary lookup, so this can never orphan existing data).
        /// </summary>
        public static void Refresh(IEnumerable<(string Branch, string Line)> activePairs)
        {
            var map = new Dictionary<string, List<string>>();
            foreach (var (branch, line) in activePairs)
            {
                if (string.IsNullOrWhiteSpace(branch) || string.IsNullOrWhiteSpace(line)) continue;
                if (!map.TryGetValue(branch, out var list))
                    map[branch] = list = new List<string>();
                list.Add(line);
            }
            _branchLines = map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        }

        /// <summary>Which Branch a given Line belongs to, or null if not recognized.</summary>
        public static string? BranchFor(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            foreach (var kv in BranchLines)
            {
                if (kv.Value.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return null;
        }

        /// <summary>
        /// The legacy Group name for a Line, used ONLY to mint the ItemId prefix
        /// (BuildPrefix takes the first letter: Commercial -> C, Residential -> R,
        /// Sustaining -> S, and so on for whatever Branches exist now).
        ///
        /// Pass 7A: Group stopped being a form field. It is derived from the LINE OF
        /// THE PERSON REGISTERING the item, so the identifier records who created it
        /// -- stable history that a later Line reassignment can't retroactively
        /// falsify. Deriving it from the existing per-user Line means no second
        /// column to keep in sync with the one that already governs visibility.
        ///
        /// Blank / unrecognised Line falls back to "Commercial", which is what all
        /// 487 pre-Pass-7 items carry, so nothing renumbers.
        /// </summary>
        public static string GroupFor(string? line)
        {
            var branch = BranchFor(line);
            if (string.IsNullOrWhiteSpace(branch)) return "Commercial";
            // "Commercial Air" -> "Commercial", "Residential Air" -> "Residential".
            // A Branch not ending in " Air" (e.g. "Sustaining") passes through as-is.
            return branch.EndsWith(" Air", StringComparison.OrdinalIgnoreCase)
                ? branch.Substring(0, branch.Length - 4)
                : branch;
        }

        public static bool IsValidLine(string? line) =>
            !string.IsNullOrWhiteSpace(line) && AllLines.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
