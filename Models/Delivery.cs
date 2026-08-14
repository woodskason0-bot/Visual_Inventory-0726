using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    // A logged incoming delivery: one photo + whatever's on the label, routed to
    // whoever should act on it. Claim lifecycle copied from VisTask verbatim:
    //   Open  --(someone claims it)-->  Claimed  --(they finish)-->  Done
    // First to claim locks their name in; everyone else sees "claimed by X".
    public class Delivery
    {
        public int Id { get; set; }

        /// <summary>Filename under DeliveryPhotoStorage.RootPath, not a full path.</summary>
        [Required, MaxLength(260)]
        public string PhotoPath { get; set; } = "";

        // All four below use the same N/A-toggle convention as Rheem PN: blank
        // and the literal "N/A" are folded together everywhere they're checked.
        [MaxLength(100)]
        public string? TrackingNumber { get; set; }

        [MaxLength(100)]
        public string? OrderNumber { get; set; }

        [MaxLength(100)]
        public string? BrandOfShipping { get; set; }

        [MaxLength(100)]
        public string? BrandOfItem { get; set; }

        /// <summary>
        /// A specific Management+ user's UserName, or UnknownRecipient for the
        /// shared org-wide bucket. Required -- the logger always picks one or
        /// the other, never leaves it blank.
        /// </summary>
        [Required, MaxLength(100)]
        public string RecipientUserName { get; set; } = "";

        /// <summary>Sentinel for "don't know whose delivery this is" -- fans out to every Management+ user instead of one person.</summary>
        public const string UnknownRecipient = "__UNKNOWN__";

        [Required, MaxLength(100)]
        public string LoggedBy { get; set; } = "";
        public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

        /// <summary>"Open" | "Claimed" | "Done".</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Open";

        public string? ClaimedBy { get; set; }
        public DateTime? ClaimedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
