using System.Linq;
using System.Collections.Generic;

namespace Visual_Inventory_System.Services
{
    /// <summary>
    /// Location name to code and back. Encode is the only place a code is made:
    /// the 1st, 3rd, 5th and last letter or digit of the name, each position once.
    /// "External Yard" -> ETRD, "RD Lab" -> RLB.
    /// </summary>
    public static class LocationCodec
    {
        public static string Encode(string? humanName)
        {
            if (string.IsNullOrWhiteSpace(humanName)) return "";

            // Keep only letters and digits, uppercased.
            var clean = new string(humanName.Where(char.IsLetterOrDigit).ToArray())
                              .ToUpperInvariant();
            if (clean.Length == 0) return "";

            int n = clean.Length;

            // 1-indexed: first, third, fifth, last. Last may equal fifth, so de-duplicate.
            var positions = new[] { 1, 3, 5, n }
                .Where(p => p >= 1 && p <= n)
                .Distinct()
                .OrderBy(p => p);

            return new string(positions.Select(p => clean[p - 1]).ToArray());
        }

        // Fallback only. The vocabulary lives in the Locations table and arrives through Refresh().
        // This list is what Decode() uses if it is called before the first Refresh.
        private static readonly string[] SeedNames = new[]
        {
            // Parents
            "RD Lab", "External Yard", "New Test Cells", "Plant Test Cells",
            // Majors
            "Metrology Mezzanine", "2-6", "Connex Area", "Trailer Area",
            // Subs
            "Samurai", "Ninja", "Connex Box 6", "Trailer 1"
        };

        // Swapped whole, never mutated, so a reader never sees a half-built map.
        private static volatile Dictionary<string, string> _reverseMap = BuildReverseMap(SeedNames);

        private static Dictionary<string, string> BuildReverseMap(IEnumerable<string> names)
        {
            var map = new Dictionary<string, string>();
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var code = Encode(name);
                if (!string.IsNullOrEmpty(code) && !map.ContainsKey(code))
                    map[code] = name.Trim();
            }
            return map;
        }

        /// <summary>Reloads the decode map. Called at startup and after any Settings location edit.</summary>
        public static void Refresh(IEnumerable<string> names)
        {
            _reverseMap = BuildReverseMap(names ?? System.Array.Empty<string>());
        }

        /// <summary>Friendly name for a code. Unknown codes, friendly names, "0" and blanks come back unchanged.</summary>
        public static string Decode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return code ?? "";
            return _reverseMap.TryGetValue(code.Trim(), out var name) ? name : code;
        }

        public static IReadOnlyDictionary<string, string> DecodeMap => _reverseMap;

        /// <summary>A stack's location as a person reads it: "RD Lab › Metrology Mezzanine · R8/2". Falls back to the FDA string.</summary>
        public static string FriendlyPath(string? parent, string? major, string? sub,
            string? rack, string? row, string? fdaString)
        {
            static bool Real(string? s) => !string.IsNullOrWhiteSpace(s) && s.Trim() != "0";
            var crumbs = new List<string>();
            if (Real(parent)) crumbs.Add(Decode(parent));
            if (Real(major)) crumbs.Add(Decode(major));
            if (Real(sub)) crumbs.Add(Decode(sub));
            string path = string.Join(" › ", crumbs);
            // "R" prefixes a bare rack number only: "8" -> "R8".
            // Named racks ("RACK 5", "Lean-To", "FLOOR") stay as stored.
            string rk = Real(rack) ? rack!.Trim() : "-";
            if (rk.Length > 0 && char.IsDigit(rk[0])) rk = "R" + rk;
            string rr = (Real(rack) || Real(row))
                ? $"{rk}/{(Real(row) ? row!.Trim() : "-")}"
                : "";
            if (path == "" && rr == "") return string.IsNullOrWhiteSpace(fdaString) ? "-" : fdaString!;
            return rr == "" ? path : (path == "" ? rr : $"{path} · {rr}");
        }
    }
}
