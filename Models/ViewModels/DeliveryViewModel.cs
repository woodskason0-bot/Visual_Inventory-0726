using System;

namespace Visual_Inventory_System.Models.ViewModels
{
    public class DeliveryViewModel
    {
        public int Id { get; set; }
        public string PhotoUrl { get; set; } = "";
        public string? TrackingNumber { get; set; }
        public string? OrderNumber { get; set; }
        public string? BrandOfShipping { get; set; }
        public string? BrandOfItem { get; set; }
        public bool IsUnknownBucket { get; set; }
        public string RecipientLabel { get; set; } = "";
        public string LoggedBy { get; set; } = "";
        public DateTime LoggedAt { get; set; }
        public string Status { get; set; } = "";
        public string? ClaimedBy { get; set; }
        public DateTime? ClaimedAt { get; set; }
    }
}
