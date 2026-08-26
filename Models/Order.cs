using System;
using System.Collections.Generic;

namespace Visual_Inventory_System.Models
{
    public class Order
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = "NEW";

        // Who placed the request, who fulfilled it, and when.
        public string? RequestedBy { get; set; }
        public string? FulfilledBy { get; set; }
        public DateTime? FulfilledAt { get; set; }

        /// <summary>
        /// Set when this order exists because a line on an earlier order didn't
        /// fully ship there -- either a picker deliberately capped the pickup and
        /// deferred the rest (OrderItem.Status "Split" on the original line), or
        /// ReportShortPull's shelf-count correction reissued the real quantity.
        /// Loose reference (no FK), same convention as TransactionLog.ItemId --
        /// purely for showing the lineage back to whoever's reading Order History.
        /// </summary>
        public int? SplitFromOrderId { get; set; }

        public List<OrderItem> Items { get; set; } = new();
    }
}
