using System;
using System.Collections.Generic;

namespace Visual_Inventory_System.Models.ViewModels
{
    // Internal Transfers section. MyRequests = my own, full history.
    // AwaitingApproval = Requested rows against items my Line or Team owns.
    public class TransferSectionViewModel
    {
        public List<TransferLineViewModel> MyRequests { get; set; } = new();
        public List<TransferLineViewModel> AwaitingApproval { get; set; } = new();
    }

    public class TransferLineViewModel
    {
        public int Id { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string RheemPartNumber { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int ThermocoupledCount { get; set; }
        public string RequesterUserName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public string? Note { get; set; }
        public string? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }

        public bool IsCompressor { get; set; }
        public bool IsMotor { get; set; }

        // Offered only while Status == Requested, for the approver to pick where to pull from.
        public List<VariantChoiceViewModel> LocationChoices { get; set; } = new();

        // The requester's preferred location, resolved to a display label.
        public string? ResolvedLocationLabel { get; set; }

        // One per physical unit, in the order ApproveTransfer pulls them (PlanTransferPull).
        // Each slot offers only the serials on its own shelf. Empty for non-compressors.
        public List<TransferUnitSlotViewModel> UnitSlots { get; set; } = new();
    }

    /// <summary>One physical unit of a pending transfer: which shelf it comes off, and what's tracked there.</summary>
    public class TransferUnitSlotViewModel
    {
        public int VariantId { get; set; }
        public string LocationLabel { get; set; } = string.Empty;
        public List<OnHandUnitViewModel> OnHandUnits { get; set; } = new();

        /// <summary>Serial this slot opens on. Staggered so slots on one stack do not all default to the same unit.</summary>
        public string? DefaultSerial { get; set; }
    }
}
