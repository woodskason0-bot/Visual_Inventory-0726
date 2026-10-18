using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// One whole AC unit on the Unit Tracker's holdover list -- a plain list people
    /// can start filling now, while the full tracker (tag, decoding, statuses) is
    /// still being scoped. The full tracker EXTENDS this table later, so nothing
    /// typed here has to be moved; that is why it is named TrackedUnit and not
    /// Unit, which collides with the CompressorUnit / MotorUnit / UnitStatus
    /// vocabulary.
    ///
    /// Information only: units can't be ordered, aren't counted anywhere else in
    /// VIS, and don't appear in Search Center. No minted ID -- the row's own Id is
    /// the reference (shown as #12) until the tag rule exists.
    ///
    /// Branch is NOT stored. Line is what VIS stores everywhere, so Branch is
    /// derived for display with OrgStructure.BranchFor(Line), the same way items
    /// do it; storing both would be a second field to keep in sync.
    ///
    /// Non-nullable strings default to "" (same as InventoryItem) so queries need
    /// no null checks. Every text rule beyond length (trim, Line/Team validity,
    /// Package/Split) is enforced in TrackedUnitService, not here.
    /// </summary>
    public class TrackedUnit
    {
        public int Id { get; set; }

        /// <summary>Free text typed by whoever registers the unit. Trimmed, case kept.</summary>
        [Required, MaxLength(60)]
        public string ModelNumber { get; set; } = "";

        /// <summary>
        /// Exactly "Package" or "Split", or "" for not recorded (the default, and what
        /// "Not set" writes). Optional on purpose: the list gets filled from sheets that
        /// don't say. Still a NOT NULL column (a non-nullable string under
        /// &lt;Nullable&gt;), so "not recorded" is "", the same as Whereabouts and
        /// BuildStatus. See TrackedUnitPackage.
        /// </summary>
        [MaxLength(10)]
        public string PackageType { get; set; } = "";

        /// <summary>Optional free text, no format rules.</summary>
        [MaxLength(60)]
        public string SerialNumber { get; set; } = "";

        /// <summary>Optional free text, no format rules (e.g. "Sample 1a").</summary>
        [MaxLength(30)]
        public string SampleNumber { get; set; } = "";

        /// <summary>One of OrgStructure.AllLines when it was set. Plain string like everywhere else Line appears -- no FK.</summary>
        [Required, MaxLength(100)]
        public string Line { get; set; } = "";

        /// <summary>An active Teams.Name when it was set, or "" (N/A).</summary>
        [MaxLength(100)]
        public string Team { get; set; } = "";

        /// <summary>Free text, multi-line. Editable by every signed-in user.</summary>
        [MaxLength(2000)]
        public string Notes { get; set; } = "";

        /// <summary>
        /// Where the unit is, when someone knows: exactly "In Lab", "In Storage" or
        /// "Outbound", or "" for not recorded (the default, and what unticking
        /// writes). One value at a time. Deliberately NOT called Location or Status:
        /// the full Unit Tracker spec already uses Status (On Hand / Transferred
        /// Out / Scrapped) and Parent/Major/Sub for the real shelf location, and
        /// this is a quick yes-I-know flag, not either of those. See
        /// TrackedUnitWhereabouts.
        /// </summary>
        [MaxLength(20)]
        public string Whereabouts { get; set; } = "";

        /// <summary>
        /// Whether the unit has been built, when someone knows: exactly "Built" or
        /// "Not Built", or "" for not recorded (the default, and what unticking
        /// writes). One value at a time, and independent of Whereabouts. This is the
        /// same field name the full Unit Tracker spec uses for build state
        /// (BuildStatus), with "Built" where that spec said "Ready", so nothing typed
        /// here has to move when the full tracker extends this table. See
        /// TrackedUnitBuildStatus.
        /// </summary>
        [MaxLength(20)]
        public string BuildStatus { get; set; } = "";

        /// <summary>UTC, set on Add, never changed.</summary>
        public DateTime RecordedAt { get; set; }

        [MaxLength(100)]
        public string RecordedBy { get; set; } = "";

        /// <summary>UTC, set on every saved change. Null until the first one.</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>"" until the first change.</summary>
        [MaxLength(100)]
        public string UpdatedBy { get; set; } = "";
    }

    /// <summary>
    /// Allowed TrackedUnit.PackageType values (blank means not recorded). Plain strings,
    /// same shape as UnitStatus.
    /// </summary>
    public static class TrackedUnitPackage
    {
        public const string Package = "Package";
        public const string Split = "Split";
        public static readonly string[] All = { Package, Split };

        /// <summary>The list filter's "nothing recorded" choice. Never stored.</summary>
        public const string NotSet = "none";

        /// <summary>The canonical spelling of a value (case-insensitive in), or null if it isn't Package or Split (blank included).</summary>
        public static string? Canonical(string? value)
        {
            var v = (value ?? "").Trim();
            return All.FirstOrDefault(p => string.Equals(p, v, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Allowed TrackedUnit.Whereabouts values (blank means not recorded). The page
    /// renders its three boxes from All, so a fourth option is one line here.
    /// </summary>
    public static class TrackedUnitWhereabouts
    {
        public const string InLab = "In Lab";
        public const string InStorage = "In Storage";
        public const string Outbound = "Outbound";
        public static readonly string[] All = { InLab, InStorage, Outbound };

        /// <summary>The list filter's "nothing recorded" choice. Never stored.</summary>
        public const string NotSet = "none";

        /// <summary>The canonical spelling of a value (case-insensitive in), or null if it isn't one of the three.</summary>
        public static string? Canonical(string? value)
        {
            var v = (value ?? "").Trim();
            return All.FirstOrDefault(w => string.Equals(w, v, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Allowed TrackedUnit.BuildStatus values (blank means not recorded). Same shape
    /// as TrackedUnitWhereabouts; the page renders its two boxes from All.
    /// </summary>
    public static class TrackedUnitBuildStatus
    {
        public const string Built = "Built";
        public const string NotBuilt = "Not Built";
        public static readonly string[] All = { Built, NotBuilt };

        /// <summary>The list filter's "nothing recorded" choice. Never stored.</summary>
        public const string NotSet = "none";

        /// <summary>The canonical spelling of a value (case-insensitive in), or null if it isn't one of the two.</summary>
        public static string? Canonical(string? value)
        {
            var v = (value ?? "").Trim();
            return All.FirstOrDefault(s => string.Equals(s, v, StringComparison.OrdinalIgnoreCase));
        }
    }
}
