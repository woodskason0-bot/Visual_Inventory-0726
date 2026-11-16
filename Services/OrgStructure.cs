using System;
using System.Collections.Generic;
using System.Linq;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Branches and their Lines, cached from the Branches and OrgLines tables. Static, no db access.
    /// Refresh() swaps the whole map at startup and after any Settings edit.
    /// </summary>
    public static class OrgStructure
    {
        // Used only until the first Refresh(). Matches the migration's seed data.
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

        /// <summary>Reloads from the tables. Pass active branches with active lines only.</summary>
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
        /// The legacy Group name for a Line. Only used to mint the ItemId prefix letter.
        /// Derived from the registering person's Line. Blank or unknown = "Commercial".
        /// </summary>
        public static string GroupFor(string? line)
        {
            var branch = BranchFor(line);
            if (string.IsNullOrWhiteSpace(branch)) return "Commercial";
            // "Commercial Air" -> "Commercial". A branch not ending in " Air" passes through.
            return branch.EndsWith(" Air", StringComparison.OrdinalIgnoreCase)
                ? branch.Substring(0, branch.Length - 4)
                : branch;
        }

        public static bool IsValidLine(string? line) =>
            !string.IsNullOrWhiteSpace(line) && AllLines.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
