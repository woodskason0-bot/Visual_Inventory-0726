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
    }
}
