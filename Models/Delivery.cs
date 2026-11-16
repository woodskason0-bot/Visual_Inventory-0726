using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    // One logged incoming delivery: a photo plus what is on the label.
    // Open -> Claimed -> Done. First claim locks the name in.
    public class Delivery
    {
        public int Id { get; set; }

        /// <summary>Filename under DeliveryPhotoStorage.RootPath, not a full path.</summary>
        [Required, MaxLength(260)]
        public string PhotoPath { get; set; } = "";

        // Blank and "N/A" mean the same thing in all four.
        [MaxLength(100)]
        public string? TrackingNumber { get; set; }

        [MaxLength(100)]
        public string? OrderNumber { get; set; }

        [MaxLength(100)]
        public string? BrandOfShipping { get; set; }

        [MaxLength(100)]
        public string? BrandOfItem { get; set; }

        /// <summary>A Management+ UserName, or UnknownRecipient. Never blank.</summary>
        [Required, MaxLength(100)]
        public string RecipientUserName { get; set; } = "";

        /// <summary>"Don't know whose": goes to every Management+ user.</summary>
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
