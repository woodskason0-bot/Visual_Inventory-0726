using System.Collections.Generic;

namespace InventoryDevTwo.Models.ViewModels
{
    public class PendingOrderItemViewModel
    {
        public int OrderItemId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int AvailableForThisOrder { get; set; }

        // Engineer's requested pull location, pre-resolved to a display label
        // ("V1 — RD Lab › ... · Qty 8"). Null = no preference recorded.
        public int? RequestedVariantId { get; set; }
        public string? RequestedLabel { get; set; }

        // All active locations for this item; the pickup person chooses from
        // these when the engineer said "Either" and the item is split.
        public List<VariantChoiceViewModel> LocationChoices { get; set; } = new();
    }

    public class VariantChoiceViewModel
    {
        public int VariantId { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
