using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// Item family, e.g. "CCR-0001": identity, ownership, alert threshold. Stock is in
    /// ItemVariant. [NotMapped] members are computed: never use them in an EF query.
    /// </summary>
    public class InventoryItem
    {
        public int Id { get; set; }          // DB PK
        public string ItemId { get; set; } = "";   // Business/FAMILY ID, e.g. "CVE-0042"

        // Label part number. One per family, unique unless blank.
        public string RheemPartNumber { get; set; } = "";

        public string ItemName { get; set; } = "";
        public string Type { get; set; } = "";
        public string Brand { get; set; } = "";
        public string Description { get; set; } = "";

        public DateTime? LastUpdated { get; set; }
        public string UpdatedBy { get; set; } = "";

        // Nullable: rows older than the column.
        public DateTime? RegisteredAt { get; set; }

        public string Team { get; set; } = "";
        public string Group { get; set; } = "";
        public string ProjectCode { get; set; } = "";

        // One of OrgStructure.AllLines. Gates visibility. "" = visible to all.
        public string Line { get; set; } = "";

        // Fires on the item total, all locations summed.
        public int AlertThreshold { get; set; } = 0;

        /// <summary>One per location. Includes retired rows.</summary>
        public List<ItemVariant> Variants { get; set; } = new();

        // Computed over the variants. Setters only stage values for CreateItem.

        /// <summary>Non-retired variants.</summary>
        [NotMapped]
        public IEnumerable<ItemVariant> ActiveVariants =>
            (Variants ?? new List<ItemVariant>()).Where(v => !v.IsRetired);

        /// <summary>Lowest-numbered active variant. Null = no active stock rows.</summary>
        [NotMapped]
        public ItemVariant? PrimaryVariant =>
            ActiveVariants.OrderBy(v => v.VariantNumber).FirstOrDefault();

        // Sum of active variants.
        private int _stagedQuantity;
        [NotMapped]
        public int Quantity
        {
            get => ActiveVariants.Any() ? ActiveVariants.Sum(v => v.Quantity) : _stagedQuantity;
            set => _stagedQuantity = value;
        }

        // Thermocoupled subset, active variants summed.
        [NotMapped]
        public int TotalThermocoupled => ActiveVariants.Sum(v => v.ThermocoupledQty);

        // Location fields read the primary variant.
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
