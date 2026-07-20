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
        public List<PendingOrderItemViewModel> Items { get; set; } = new();
        public bool CanFulfill { get; set; }
        public bool IsBlockedByPriority { get; set; }
    }
}