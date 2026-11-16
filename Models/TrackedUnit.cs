using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// One whole AC unit on the Unit Tracker list. Information only: not orderable,
    /// not counted in stock, not shown in Search Center.
    /// </summary>
    public class TrackedUnit
    {
        public int Id { get; set; }

        [Required, MaxLength(60)]
        public string ModelNumber { get; set; } = "";

        /// <summary>"Package" | "Split" | "" (not recorded). See TrackedUnitPackage.</summary>
        [MaxLength(10)]
        public string PackageType { get; set; } = "";

        [MaxLength(60)]
        public string SerialNumber { get; set; } = "";

        [MaxLength(30)]
        public string SampleNumber { get; set; } = "";

        /// <summary>One of OrgStructure.AllLines. Branch is derived, never stored.</summary>
        [Required, MaxLength(100)]
        public string Line { get; set; } = "";

        /// <summary>Active Teams.Name when set, or "" (N/A).</summary>
        [MaxLength(100)]
        public string Team { get; set; } = "";

        /// <summary>Free text, multi-line. Any signed-in user can edit.</summary>
        [MaxLength(2000)]
        public string Notes { get; set; } = "";

        /// <summary>"In Lab" | "In Storage" | "Outbound" | "" (not recorded). See TrackedUnitWhereabouts.</summary>
        [MaxLength(20)]
        public string Whereabouts { get; set; } = "";

        /// <summary>"Built" | "Not Built" | "" (not recorded). See TrackedUnitBuildStatus.</summary>
        [MaxLength(20)]
        public string BuildStatus { get; set; } = "";

        /// <summary>UTC, set on Add, never changed.</summary>
        public DateTime RecordedAt { get; set; }

        [MaxLength(100)]
        public string RecordedBy { get; set; } = "";

        /// <summary>UTC, set on every save. Null until the first.</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>"" until the first change.</summary>
        [MaxLength(100)]
        public string UpdatedBy { get; set; } = "";
    }

    /// <summary>Allowed TrackedUnit.PackageType values. Blank = not recorded.</summary>
    public static class TrackedUnitPackage
    {
        public const string Package = "Package";
        public const string Split = "Split";
        public static readonly string[] All = { Package, Split };

        /// <summary>List filter's "nothing recorded" choice. Never stored.</summary>
        public const string NotSet = "none";

        /// <summary>Canonical spelling, case-insensitive in. Null if not Package or Split (blank included).</summary>
        public static string? Canonical(string? value)
        {
            var v = (value ?? "").Trim();
            return All.FirstOrDefault(p => string.Equals(p, v, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Allowed TrackedUnit.Whereabouts values. The page renders its boxes from All.</summary>
    public static class TrackedUnitWhereabouts
    {
        public const string InLab = "In Lab";
        public const string InStorage = "In Storage";
        public const string Outbound = "Outbound";
        public static readonly string[] All = { InLab, InStorage, Outbound };

        /// <summary>List filter's "nothing recorded" choice. Never stored.</summary>
        public const string NotSet = "none";

        /// <summary>Canonical spelling, case-insensitive in. Null if not one of the three.</summary>
        public static string? Canonical(string? value)
        {
            var v = (value ?? "").Trim();
            return All.FirstOrDefault(w => string.Equals(w, v, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Allowed TrackedUnit.BuildStatus values. The page renders its boxes from All.</summary>
    public static class TrackedUnitBuildStatus
    {
        public const string Built = "Built";
        public const string NotBuilt = "Not Built";
        public static readonly string[] All = { Built, NotBuilt };

        /// <summary>List filter's "nothing recorded" choice. Never stored.</summary>
        public const string NotSet = "none";

        /// <summary>Canonical spelling, case-insensitive in. Null if not one of the two.</summary>
        public static string? Canonical(string? value)
        {
            var v = (value ?? "").Trim();
            return All.FirstOrDefault(s => string.Equals(s, v, StringComparison.OrdinalIgnoreCase));
        }
    }
}
