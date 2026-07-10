using System;

namespace InventoryDevTwo.Models
{
    public class Notification
    {
        public int Id { get; set; }

        // Who should see this. One row per recipient (fan-out on write).
        public string RecipientUserName { get; set; } = "";

        // "OrderSubmitted" | "PickupRequested" | "Fulfilled" -- drives the bell icon/color.
        public string Category { get; set; } = "";

        public string Message { get; set; } = "";

        // Optional deep-link, e.g. "/Home/OrderDetails/5". Null = no link.
        public string? LinkUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; }
        public bool IsDismissed { get; set; }
    }
}
