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
            ["Residential Air"] = new[] { "Residential OD", "Residential Coils/AH", "Residential Gas Furnace" },
            ["Commercial Air"] = new[] { "Commercial Package/Splits", "Residential Package", "International" }
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

        public static bool IsValidLine(string? line) =>
            !string.IsNullOrWhiteSpace(line) && AllLines.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
