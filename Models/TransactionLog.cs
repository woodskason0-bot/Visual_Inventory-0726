using System;

namespace Visual_Inventory_System.Models
{
    public class TransactionLog
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }

        public string ActionType { get; set; } = ""; // e.g. "Scrap", "Ownership", "Order Picked Up"
        public string ItemId { get; set; } = "";

        /// <summary>
        /// Snapshot of ItemName at the moment of this transaction. The feed's
        /// primary lookup is still a live join against InventoryItems (so a
        /// later rename shows up retroactively) -- this is only the fallback
        /// for when that join comes back empty, most notably a deleted item
        /// ("lives in the audit log only" per Delete Item). Logs written
        /// before this field existed simply have it blank; the feed falls
        /// back further to "Unknown item" only then.
        /// </summary>
        public string? ItemName { get; set; }

        public int QuantityChange { get; set; }      // Negative for scrap/orders, positive for additions

        public string Details { get; set; } = "";    // Extra info (e.g. "Moved from Samurai to Ninja")
        public string User { get; set; } = "Unknown";
    }
}