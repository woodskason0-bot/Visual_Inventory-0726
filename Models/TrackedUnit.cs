using System;
using System.ComponentModel.DataAnnotations;

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

        /// <summary>Exactly "Package" or "Split". See TrackedUnitPackage.</summary>
        [Required, MaxLength(10)]
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

    /// <summary>Allowed TrackedUnit.PackageType values. Plain strings, same shape as UnitStatus.</summary>
    public static class TrackedUnitPackage
    {
        public const string Package = "Package";
        public const string Split = "Split";
        public static readonly string[] All = { Package, Split };
    }
}
