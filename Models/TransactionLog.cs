using System;

namespace Visual_Inventory_System.Models
{
    public class TransactionLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }

        public string ActionType { get; set; } = ""; // e.g. "Scrap", "Ownership", "Order Picked Up"
        public string ItemId { get; set; } = "";
        public int QuantityChange { get; set; }      // Negative for scrap/orders, positive for additions

        public string Details { get; set; } = "";    // Extra info (e.g. "Moved from Samurai to Ninja")
        public string User { get; set; } = "Unknown";
    }
}