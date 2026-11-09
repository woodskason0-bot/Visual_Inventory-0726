using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A hold on some of one item's stock (2026-11-08, docs/Unit_Reservations_Spec.md):
    /// "reserved for (Reason) by (ReservedBy) from (StartDate) to (EndDate or until
    /// notice)". A hard hold -- nobody but ReservedBy can order, pick up, transfer or
    /// scrap what it holds -- counted per stack in ReservationLines, with specific
    /// compressor / TC motor units optionally named through their ReservationId.
    ///
    /// A hold reduces AVAILABLE, never On hand: the stock stays on the shelf and in
    /// ItemVariant.Quantity until it really leaves. See StockClaims for the math.
    ///
    /// "Live" = Status Active and EndDate null or not yet passed. Every query goes
    /// through ReservationService.Live so an expired hold stops holding at midnight
    /// even before the sweep marks it Expired.
    /// </summary>
    public class Reservation
    {
        public int Id { get; set; }

        /// <summary>Business id of the item, e.g. "CCR-0029". Loose reference, no FK (same as every other table).</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        /// <summary>Free text, required. Also the purpose group: the Reserve dialog offers
        /// a person's open reasons back to them, and Reserved Items groups their holds by it.</summary>
        [Required, MaxLength(200)]
        public string Reason { get; set; } = "";

        /// <summary>First.Last of whoever made it -- always the signed-in person.</summary>
        [Required, MaxLength(100)]
        public string ReservedBy { get; set; } = "";

        /// <summary>Local date it's needed from. Informational: the hold starts when it's made.</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Local date; the hold lasts through this day. Null = until notice.</summary>
        public DateTime? EndDate { get; set; }

        public DateTime CreatedAt { get; set; }

        /// <summary>See ReservationStatus.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = ReservationStatus.Active;

        public DateTime? EndedAt { get; set; }

        [MaxLength(100)]
        public string? EndedBy { get; set; }

        [MaxLength(300)]
        public string? EndNote { get; set; }

        /// <summary>The "ends today" notification has gone to the reserver.</summary>
        public bool EndingNoticeSent { get; set; }

        public List<ReservationLine> Lines { get; set; } = new();
    }

    /// <summary>How much of one stack a reservation holds. TcQuantity is the thermocoupled
    /// part (motors only) and never exceeds Quantity.</summary>
    public class ReservationLine
    {
        public int Id { get; set; }

        public int ReservationId { get; set; }
        public Reservation Reservation { get; set; } = null!;

        /// <summary>The ItemVariant (stack). Loose reference, same as CompressorUnit.ItemVariantId.</summary>
        public int ItemVariantId { get; set; }

        public int Quantity { get; set; }

        public int TcQuantity { get; set; }
    }

    public static class ReservationStatus
    {
        public const string Active = "Active";
        public const string Released = "Released";
        public const string Expired = "Expired";
        public const string PickedUp = "Picked Up";
    }
}
