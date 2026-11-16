using System;
using System.Collections.Generic;

namespace Visual_Inventory_System.Models.ViewModels
{
    public class PendingOrderViewModel
    {
        public int OrderId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? RequestedBy { get; set; }
        public int? SplitFromOrderId { get; set; }
        public List<PendingOrderItemViewModel> Items { get; set; } = new();
        public bool CanFulfill { get; set; }
        public bool IsBlockedByPriority { get; set; }
        // Lines held for the orderer's Non-TC Motor answer. Not pickable until answered.
        public List<WaitingLineViewModel> WaitingLines { get; set; } = new();
    }

    public class WaitingLineViewModel
    {
        public int OrderItemId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int TcAsked { get; set; }
        public string Note { get; set; } = string.Empty;
        public bool CanAnswer { get; set; }
    }
}