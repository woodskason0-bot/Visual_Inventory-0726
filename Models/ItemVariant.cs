using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Visual_Inventory_System.Models
{
    /// <summary>One stack of a family at one location. Retired, never deleted.</summary>
    public class ItemVariant
    {
        public int Id { get; set; }          // DB PK

        [ForeignKey("InventoryItem")]
        public int InventoryItemId { get; set; }
        public InventoryItem InventoryItem { get; set; } = null!;

        /// <summary>1 = original. Counts up per new stack.</summary>
        public int VariantNumber { get; set; } = 1;

        public int Quantity { get; set; }

        /// <summary>Owning team. Defaults to the item's. A second team's share = a second variant.</summary>
        public string Team { get; set; } = "";

        /// <summary>Thermocoupled subset of Quantity. Always 0 &lt;= ThermocoupledQty &lt;= Quantity.</summary>
        public int ThermocoupledQty { get; set; } = 0;

        // Parent/Major/Sub: codes (see LocationCodec). Rack/Row: free text.
        public string Parent { get; set; } = "";
        public string Major { get; set; } = "";
        public string Sub { get; set; } = "";
        public string Rack { get; set; } = "";
        public string Row { get; set; } = "";
        public string FdaString { get; set; } = "";

        /// <summary>First seen. Nullable: backfilled rows inherit the item's, which may be null.</summary>
        public DateTime? RegisteredAt { get; set; }

        /// <summary>Stock fully merged elsewhere. Kept for history, out of totals and displays.</summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>e.g. "CCR-0001-02". Needs the InventoryItem nav loaded.</summary>
        [NotMapped]
        public string DisplayId =>
            InventoryItem != null ? $"{InventoryItem.ItemId}-{VariantNumber:D2}" : $"?-{VariantNumber:D2}";
    }
}
