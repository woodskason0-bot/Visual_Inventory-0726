using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace InventoryDevTwo.Models
{
    /// <summary>
    /// The item FAMILY record (e.g. "CCR-0001"). Holds everything that is true
    /// regardless of where the physical stock sits: identity, ownership, alert
    /// threshold (item-TOTAL by design). Physical stock lives in ItemVariant
    /// children -- one per location -- and Quantity/location on this class are
    /// now computed pass-throughs over the active (non-retired) variants.
    ///
    /// PASS-THROUGH BRIDGE: the flat properties (Quantity, Parent, Major,
    /// Sub, Rack, Row, FdaString) are computed facades over the variants.
    /// Reads resolve against the PRIMARY variant (lowest active number) or
    /// the active-variant SUM (Quantity); writes
    /// land in staging fields that CreateItem uses to build variant 1. These
    /// are [NotMapped]: NEVER use them inside an EF query (Where/Select/Sum) --
    /// they cannot translate to SQL. Query ItemVariants directly instead.
    /// </summary>
    public class InventoryItem
    {
        public int Id { get; set; }          // DB PK
        public string ItemId { get; set; } = "";   // Business/FAMILY ID, e.g. "CVE-0042"

        public string ItemName { get; set; } = "";
        public string Type { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Description { get; set; } = "";

        public DateTime? LastUpdated { get; set; }
        public string UpdatedBy { get; set; } = "";

        // First-registered timestamp. Nullable so existing rows migrate cleanly
        // (they backfill from their earliest "New Registry" transaction log).
        public DateTime? RegisteredAt { get; set; }

        public string Team { get; set; } = "";
        public string Group { get; set; } = "";
        public string ProjectCode { get; set; } = "";

        // Item-TOTAL alert threshold (fires on the sum across all locations).
        public int AlertThreshold { get; set; } = 0;

        /// <summary>All physical instances of this family, one per location. Includes retired rows.</summary>
        public List<ItemVariant> Variants { get; set; } = new();

        // =====================================================================
        // PASS-THROUGH BRIDGE -- computed over variants, staged before creation
        // =====================================================================

        /// <summary>Active (non-retired) variants only.</summary>
        [NotMapped]
        public IEnumerable<ItemVariant> ActiveVariants =>
            (Variants ?? new List<ItemVariant>()).Where(v => !v.IsRetired);

        /// <summary>The anchor: lowest-numbered active variant. Null only if the item has no active stock rows.</summary>
        [NotMapped]
        public ItemVariant? PrimaryVariant =>
            ActiveVariants.OrderBy(v => v.VariantNumber).FirstOrDefault();

        // ----- Quantity: SUM of active variants; setter stages for CreateItem -----
        private int _stagedQuantity;
        [NotMapped]
        public int Quantity
        {
            get => ActiveVariants.Any() ? ActiveVariants.Sum(v => v.Quantity) : _stagedQuantity;
            set => _stagedQuantity = value;
        }

        // ----- Thermocoupled total: SUM of active variants' TC subset. -----
        // Read-only convenience for family-level display (holoviewer header).
        // [NotMapped] like Quantity -- NEVER use inside an EF query; query
        // ItemVariants.ThermocoupledQty directly for anything DB-side.
        [NotMapped]
        public int TotalThermocoupled => ActiveVariants.Sum(v => v.ThermocoupledQty);

        // ----- Location fields: read the PRIMARY variant; setters stage -----
        private string _stagedParent = "";
        [NotMapped]
        public string Parent
        {
            get => PrimaryVariant?.Parent ?? _stagedParent;
            set => _stagedParent = value ?? "";
        }

        private string _stagedMajor = "";
        [NotMapped]
        public string Major
        {
            get => PrimaryVariant?.Major ?? _stagedMajor;
            set => _stagedMajor = value ?? "";
        }

        private string _stagedSub = "";
        [NotMapped]
        public string Sub
        {
            get => PrimaryVariant?.Sub ?? _stagedSub;
            set => _stagedSub = value ?? "";
        }

        private string _stagedRack = "";
        [NotMapped]
        public string Rack
        {
            get => PrimaryVariant?.Rack ?? _stagedRack;
            set => _stagedRack = value ?? "";
        }

        private string _stagedRow = "";
        [NotMapped]
        public string Row
        {
            get => PrimaryVariant?.Row ?? _stagedRow;
            set => _stagedRow = value ?? "";
        }

        private string _stagedFdaString = "";
        [NotMapped]
        public string FdaString
        {
            get => PrimaryVariant?.FdaString ?? _stagedFdaString;
            set => _stagedFdaString = value ?? "";
        }
    }
}
