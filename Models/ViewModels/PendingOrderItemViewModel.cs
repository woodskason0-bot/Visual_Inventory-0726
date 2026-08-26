using System.Collections.Generic;

namespace Visual_Inventory_System.Models.ViewModels
{
    public class PendingOrderItemViewModel
    {
        public int OrderItemId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        // Rheem PN off the physical label -- shown so the pickup person can
        // verify the box in hand matches the line item. "" = not captured yet.
        public string RheemPartNumber { get; set; } = string.Empty;
        // Drives the per-unit Lab#/Serial# capture rows on the pickup form --
        // only rendered when this equals "Compressor" (InventoryService.IsCompressorType).
        public string? Type { get; set; }
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

        // Raw count at this location, separate from Label (which folds it into
        // a display string) -- feeds the live shortfall check client-side:
        // does this location actually cover the line's full ordered qty?
        public int Quantity { get; set; }

        // Real on-hand compressor units AT THIS LOCATION -- what the serial
        // picker on Pickup Queue actually offers once this location is
        // chosen. Empty for non-compressor lines and for a location with
        // nothing tracked yet (the untracked-unit case is the norm, not the
        // exception -- see CompressorUnit's own doc comment).
        public List<OnHandUnitViewModel> OnHandUnits { get; set; } = new();
    }

    public class OnHandUnitViewModel
    {
        public string? SerialNumber { get; set; }
        public string? LabNumber { get; set; }
    }
}
