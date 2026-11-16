using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A hard hold on some of one item's stock. Reduces Available, never On hand.
    /// Live = Active and EndDate null or not passed. Query through ReservationService.Live.
    /// </summary>
    public class Reservation
    {
        public int Id { get; set; }

        /// <summary>Item, e.g. "CCR-0029". Loose reference, no FK.</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        /// <summary>Free text, required. Also the grouping key on Reserved Items and in the Reserve dialog.</summary>
        [Required, MaxLength(200)]
        public string Reason { get; set; } = "";

        /// <summary>First.Last of the signed-in person who made it.</summary>
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

    /// <summary>How much of one stack a reservation holds. TcQuantity (motors) never exceeds Quantity.</summary>
    public class ReservationLine
    {
        public int Id { get; set; }

        public int ReservationId { get; set; }
        public Reservation Reservation { get; set; } = null!;

        /// <summary>The stack (ItemVariant.Id). Loose reference.</summary>
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
