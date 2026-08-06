using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// One row per PHYSICAL thermocoupled (TC) motor -- CompressorUnit's sibling,
    /// same roster shape and lifecycle (On Hand / Picked Up / Scrapped, see
    /// UnitStatus), built separately rather than generalizing CompressorUnit
    /// because motors have no serial number to key uniqueness on.
    ///
    /// TC-ONLY. A non-thermocoupled motor never gets a row here -- TC comes
    /// from the manufacturer, VIS doesn't do it, so only the TC subset
    /// (ItemVariant.ThermocoupledQty) is ever worth tracking per-unit.
    /// ItemVariant.ThermocoupledQty REMAINS AUTHORITATIVE for on-hand counts;
    /// this is a partial overlay exactly like CompressorUnit is over Quantity.
    ///
    /// No SerialNumber. LabNumber is recorded, never assigned (same as
    /// CompressorUnit), and won't always be present -- unlike compressors,
    /// there's no uniqueness constraint on it, since it's the only identifying
    /// field and two units without one would otherwise collide.
    /// </summary>
    public class MotorUnit
    {
        public int Id { get; set; }

        /// <summary>Business id of the owning family, e.g. "CMR-0012". Loose reference, no FK (matches CompressorUnit/TransactionLog style).</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        /// <summary>Which physical location this unit sits in. Nullable while picked up and not returned -- same reasoning as CompressorUnit.ItemVariantId.</summary>
        public int? ItemVariantId { get; set; }

        /// <summary>Rheem asset tag, e.g. "F-0042-26". Recorded, never generated. Often blank -- no numbering process exists yet.</summary>
        [MaxLength(50)]
        public string? LabNumber { get; set; }

        /// <summary>"On Hand" | "Picked Up" | "Scrapped". See UnitStatus (shared with CompressorUnit).</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = UnitStatus.OnHand;

        /// <summary>When this unit entered the roster (counted in, or first seen at pickup).</summary>
        public DateTime RecordedAt { get; set; }

        [MaxLength(100)]
        public string RecordedBy { get; set; } = "";

        // ----- Set only when the unit actually leaves the shelf -----

        public int? OrderId { get; set; }
        public int? OrderItemId { get; set; }
        public DateTime? PickedUpAt { get; set; }

        [MaxLength(100)]
        public string? PickedUpBy { get; set; }
    }
}
