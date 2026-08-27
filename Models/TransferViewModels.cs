using System;
using System.Collections.Generic;

namespace Visual_Inventory_System.Models.ViewModels
{
    // Backs the "Internal Transfers" section on My Orders: MyRequests = this
    // user's own transfer requests (full history, same shape as My Order
    // History below it -- once decided there's nothing left to act on, but the
    // outcome is still worth showing); AwaitingApproval = Requested-status rows
    // against items this user's Line/Team owns -- the queue itself, since a
    // decided row needs no more action and drops off (same convention the loan
    // bench uses).
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

        // One entry per physical unit an approval would pull, in the exact order
        // ApproveTransfer pulls them (OrderService.PlanTransferPull). Drives the
        // per-unit serial picklists: slot u offers the units actually on the
        // shelf slot u comes off, instead of every location's serials pooled
        // together -- which is what made a reasonable pick get refused by
        // AssignOneCompressorUnit's location-scoped match. Empty for
        // non-compressor lines and for anything already decided.
        public List<TransferUnitSlotViewModel> UnitSlots { get; set; } = new();
    }

    /// <summary>One physical unit of a pending transfer: which shelf it comes off, and what's tracked there.</summary>
    public class TransferUnitSlotViewModel
    {
        public int VariantId { get; set; }
        public string LocationLabel { get; set; } = string.Empty;
        public List<OnHandUnitViewModel> OnHandUnits { get; set; } = new();

        /// <summary>
        /// Which serial this slot opens on. Staggered across the slots sharing a
        /// variant -- one real serial each while they last, then null ("No
        /// serial") -- so a multi-unit approval doesn't open with every dropdown
        /// aimed at the same physical unit. Same reasoning as Pickup Queue's own
        /// staggered default; AssignOneCompressorUnit rejects the duplicate
        /// anyway, but not defaulting into the rejection is friendlier.
        /// </summary>
        public string? DefaultSerial { get; set; }
    }
}
