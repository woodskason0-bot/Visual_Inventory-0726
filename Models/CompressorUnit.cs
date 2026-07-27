using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// One row per PHYSICAL compressor, logged the moment it's actually
    /// picked up off the shelf for an order -- the "mini variant" that
    /// splits off from the family-level InventoryItem at that instant.
    /// Forward-only by design: existing/departed stock is not backfilled
    /// (Kason is zeroing out untracked compressor counts separately), so
    /// this table starts empty and only grows from pickups going forward.
    /// Both LabNumber and SerialNumber are soft-required -- ask at pickup,
    /// insert if given, leave null if not. Missing values get flagged as a
    /// summary count on the order's TransactionLog entry, not blocked here.
    /// </summary>
    public class CompressorUnit
    {
        public int Id { get; set; }

        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        public int OrderId { get; set; }
        public int? OrderItemId { get; set; }

        [MaxLength(50)]
        public string? LabNumber { get; set; }

        [MaxLength(50)]
        public string? SerialNumber { get; set; }

        public DateTime PickedUpAt { get; set; }

        [MaxLength(100)]
        public string PickedUpBy { get; set; } = "";
    }
}
