using System.Collections.Generic;

namespace Visual_Inventory_System.Models.ViewModels
{
    public class PendingOrderItemViewModel
    {
        public int OrderItemId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        // Label part number, so the picker can check the box in hand. "" = not captured.
        public string RheemPartNumber { get; set; } = string.Empty;
        // "Compressor" turns on the per-unit Lab#/Serial# rows on the pickup form.
        public string? Type { get; set; }
        public int Quantity { get; set; }
        public int AvailableForThisOrder { get; set; }

        // Requested pull location and its display label. Null = no preference.
        public int? RequestedVariantId { get; set; }
        public string? RequestedLabel { get; set; }

        // All active locations. The picker chooses when the request said "Either".
        public List<VariantChoiceViewModel> LocationChoices { get; set; } = new();
    }

    public class VariantChoiceViewModel
    {
        public int VariantId { get; set; }
        public string Label { get; set; } = string.Empty;

        // Raw count here, apart from Label. Feeds the client-side shortfall check.
        public int Quantity { get; set; }

        // On-hand compressor units at this location, for the serial picker. Often empty.
        public List<OnHandUnitViewModel> OnHandUnits { get; set; } = new();
    }

    public class OnHandUnitViewModel
    {
        public string? SerialNumber { get; set; }
        public string? LabNumber { get; set; }
    }
}
