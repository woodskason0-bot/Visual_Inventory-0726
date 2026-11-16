using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>One physical compressor. Overlay only: ItemVariant.Quantity is the count of record.</summary>
    public class CompressorUnit
    {
        public int Id { get; set; }

        /// <summary>Owning item, e.g. "CCR-0029". Loose reference, no FK.</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        /// <summary>Stack it sits on. Null while picked up, set again on return.</summary>
        public int? ItemVariantId { get; set; }

        /// <summary>Manufacturer serial. Unique per (ItemId, SerialNumber): LG reuses serials across models.</summary>
        [MaxLength(50)]
        public string? SerialNumber { get; set; }

        /// <summary>Rheem asset tag, e.g. "F3860-25". Recorded, never generated, not unique.</summary>
        [MaxLength(50)]
        public string? LabNumber { get; set; }

        /// <summary>"On Hand" | "Picked Up" | "Scrapped". See UnitStatus.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = UnitStatus.OnHand;

        /// <summary>Entered the roster: counted in, or first seen at pickup.</summary>
        public DateTime RecordedAt { get; set; }

        [MaxLength(100)]
        public string RecordedBy { get; set; } = "";

        // Set only when the unit leaves the shelf.

        /// <summary>Null while On Hand.</summary>
        public int? OrderId { get; set; }
        public int? OrderItemId { get; set; }
        public DateTime? PickedUpAt { get; set; }

        [MaxLength(100)]
        public string? PickedUpBy { get; set; }

        /// <summary>Left by transfer approval: set instead of OrderId/OrderItemId. Never both.</summary>
        public int? TransferRequestId { get; set; }

        /// <summary>Reservation naming this On Hand unit, or null. Read through ReservationService only.</summary>
        public int? ReservationId { get; set; }
    }

    /// <summary>Allowed CompressorUnit.Status values.</summary>
    public static class UnitStatus
    {
        public const string OnHand = "On Hand";
        public const string PickedUp = "Picked Up";
        public const string Scrapped = "Scrapped";

        public static readonly string[] All = { OnHand, PickedUp, Scrapped };
    }
}
