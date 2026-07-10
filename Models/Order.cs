using System;
using System.Collections.Generic;

namespace InventoryDevTwo.Models
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

        public List<OrderItem> Items { get; set; } = new();
    }
}
