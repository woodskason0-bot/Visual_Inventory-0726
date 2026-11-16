using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A request to transfer another Line's stock. Parallel to Order, never through the cart.
    /// One-way, not a loan: once decided, nothing more happens to the row.
    /// </summary>
    public class TransferRequest
    {
        public int Id { get; set; }

        /// <summary>Item, e.g. "CCR-0029". Loose reference, no FK.</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        public int Quantity { get; set; }

        /// <summary>Units requested thermocoupled. 0 for non-motors.</summary>
        public int ThermocoupledCount { get; set; } = 0;

        /// <summary>Preferred stack (ItemVariant.Id), not a lock. Approval spills into other stacks if short.</summary>
        public int? RequestedVariantId { get; set; }

        [Required, MaxLength(100)]
        public string RequesterUserName { get; set; } = "";

        /// <summary>"Requested" | "Approved" | "Denied". See TransferStatus.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = TransferStatus.Requested;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? Note { get; set; }

        // Set once decided.

        [MaxLength(100)]
        public string? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }
    }

    /// <summary>Allowed TransferRequest.Status values.</summary>
    public static class TransferStatus
    {
        public const string Requested = "Requested";
        public const string Approved = "Approved";
        public const string Denied = "Denied";
    }
}
