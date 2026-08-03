using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// One row per PHYSICAL compressor -- a ROSTER of individual units, not a
    /// pickup log.
    ///
    /// Pass 4 created rows only at pickup, which meant stock sitting on a shelf
    /// had nowhere to live. Pass 6A inverts that: a unit is recorded when it is
    /// KNOWN (counted in, or captured at pickup) and annotated as it moves.
    ///
    /// QUANTITY REMAINS AUTHORITATIVE. ItemVariant.Quantity is the truth about
    /// how much stock exists; units are a PARTIAL OVERLAY that fills in over
    /// time. As of the 7/28 intake only 183 of 825 compressors have a serial,
    /// so unit rows must never drive counts -- a pull with no matching unit row
    /// is normal, not an error.
    ///
    /// Serial and LabNumber are BOTH unit-level and permanent (a manufacturer
    /// serial and Rheem's own asset tag for the same physical machine). Lab
    /// numbers are recorded only -- VIS does not assign them, pending a
    /// numbering process that will span other item types too.
    /// </summary>
    public class CompressorUnit
    {
        public int Id { get; set; }

        /// <summary>Business id of the owning family, e.g. "CCR-0029". Loose reference, no FK (matches TransactionLog style).</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        /// <summary>
        /// Which physical location this unit sits in -- the ItemVariant row.
        /// Nullable: a unit that has been picked up and not returned isn't on
        /// any shelf. Set again on return. Without this, "where is this exact
        /// compressor" is unanswerable for any model stocked in two places
        /// (18 models are, as of the 7/28 intake).
        /// </summary>
        public int? ItemVariantId { get; set; }

        /// <summary>Manufacturer serial. LG reuses these ACROSS models, so uniqueness is (ItemId, SerialNumber) -- not serial alone.</summary>
        [MaxLength(50)]
        public string? SerialNumber { get; set; }

        /// <summary>Rheem asset tag, e.g. "F3860-25". Recorded, never generated. Deliberately unconstrained -- no uniqueness -- until a process exists.</summary>
        [MaxLength(50)]
        public string? LabNumber { get; set; }

        /// <summary>"On Hand" | "Picked Up" | "Scrapped". See UnitStatus.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = UnitStatus.OnHand;

        /// <summary>When this unit entered the roster (counted in, or first seen at pickup).</summary>
        public DateTime RecordedAt { get; set; }

        [MaxLength(100)]
        public string RecordedBy { get; set; } = "";

        // ----- Set only when the unit actually leaves the shelf -----

        /// <summary>Null while the unit is On Hand. Was NOT NULL before Pass 6A, which is what blocked counted-in stock.</summary>
        public int? OrderId { get; set; }
        public int? OrderItemId { get; set; }
        public DateTime? PickedUpAt { get; set; }

        [MaxLength(100)]
        public string? PickedUpBy { get; set; }
    }

    /// <summary>Allowed CompressorUnit.Status values. Plain strings, matching the loose-string style used for ActionType and Line.</summary>
    public static class UnitStatus
    {
        public const string OnHand = "On Hand";
        public const string PickedUp = "Picked Up";
        public const string Scrapped = "Scrapped";

        public static readonly string[] All = { OnHand, PickedUp, Scrapped };
    }
}
