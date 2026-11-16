using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// One physical thermocoupled motor. TC only: a plain motor never gets a row.
    /// Overlay only: ItemVariant.ThermocoupledQty is the count of record.
    /// </summary>
    public class MotorUnit
    {
        public int Id { get; set; }

        /// <summary>Owning item, e.g. "CMR-0012". Loose reference, no FK.</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        /// <summary>Stack it sits on. Null while picked up, set again on return.</summary>
        public int? ItemVariantId { get; set; }

        /// <summary>Rheem asset tag, e.g. "F-0042-26". Recorded, never generated. Often blank, not unique.</summary>
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
}
