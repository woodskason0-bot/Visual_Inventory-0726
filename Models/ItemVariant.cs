using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryDevTwo.Models
{
    /// <summary>
    /// One physical instance of an item family at one location.
    /// The InventoryItem row is the "family" (CCR-0001); each ItemVariant is a
    /// numbered member of that family (-01, -02, ...) with its own location,
    /// quantity, and FDA string. Variant 1 is the original/anchor.
    ///
    /// Retirement, not deletion: when a variant's stock is fully merged into
    /// another variant (its location drains to 0), it is marked IsRetired
    /// rather than deleted, so historical logs/orders that reference it still
    /// resolve to a real row. Retired numbers may be reused by new variants;
    /// logs snapshot their details at write time for exactly this reason.
    /// </summary>
    public class ItemVariant
    {
        public int Id { get; set; }          // DB PK

        [ForeignKey("InventoryItem")]
        public int InventoryItemId { get; set; }
        public InventoryItem InventoryItem { get; set; } = null!;

        /// <summary>1 = original/anchor; grows as new physical instances register or split off.</summary>
        public int VariantNumber { get; set; } = 1;

        public int Quantity { get; set; }

        /// <summary>
        /// How many of this variant's Quantity are thermocoupled (a marked
        /// subset, not a separate pile). INVARIANT: 0 &lt;= ThermocoupledQty
        /// &lt;= Quantity, always. Motors only in practice; non-motor variants
        /// simply leave it 0 so the invariant holds uniformly.
        /// </summary>
        public int ThermocoupledQty { get; set; } = 0;

        // Location (Parent/Major/Sub stored as CODES -- see LocationCodec;
        // Rack/Row free-form team-assigned values; FdaString is the joined code path).
        public string Parent { get; set; } = "";
        public string Major { get; set; } = "";
        public string Sub { get; set; } = "";
        public string Rack { get; set; } = "";
        public string Row { get; set; } = "";
        public string FdaString { get; set; } = "";

        /// <summary>When this physical instance first appeared. Nullable so backfilled rows can inherit the item's (possibly null) RegisteredAt.</summary>
        public DateTime? RegisteredAt { get; set; }

        /// <summary>True once this variant's stock has been fully merged elsewhere. Kept for history; excluded from totals and displays.</summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>Display form, e.g. "CCR-0001-02". Requires the InventoryItem nav to be loaded.</summary>
        [NotMapped]
        public string DisplayId =>
            InventoryItem != null ? $"{InventoryItem.ItemId}-{VariantNumber:D2}" : $"?-{VariantNumber:D2}";
    }
}
