using System;
using System.Collections.Generic;
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Fixed, non-manageable org structure: 2 Branches, 6 Lines total (3 per
    /// Branch). No add/remove UI -- these names don't change. Plain strings
    /// on User.Line / InventoryItem.Line (not FK'd to a table) so ad-hoc SQL
    /// against the DB stays simple: UPDATE InventoryItems SET Line = 'X'.
    /// </summary>
    public static class OrgStructure
    {
        public static readonly Dictionary<string, string[]> BranchLines = new()
        {
            ["Residential Air"] = new[] { "Residential OD", "Residential Coils/AH", "Residential Gas Furnaces" },
            ["Commercial Air"] = new[] { "Commercial Packaged/Splits", "Residential Packaged", "International" }
        };

        public static readonly string[] AllLines = BranchLines.Values.SelectMany(x => x).ToArray();

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
        /// (BuildPrefix takes the first letter: Commercial -> C, Residential -> R).
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
            return branch.EndsWith(" Air", StringComparison.OrdinalIgnoreCase)
                ? branch.Substring(0, branch.Length - 4)
                : branch;
        }

        public static bool IsValidLine(string? line) =>
            !string.IsNullOrWhiteSpace(line) && AllLines.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
