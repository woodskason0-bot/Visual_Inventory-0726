using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// An internal cross-Line/cross-Team transfer request against another
    /// Line's item, found via Search Center once Search stopped filtering
    /// results to the viewer's own Line. Deliberately parallel to Order/
    /// OrderItem, not built on them -- a transfer never touches the cart/order
    /// pipeline, so an item can be ordered by its own Line and transferred to
    /// another at the same time without either path needing to know about the
    /// other beyond the shared ItemVariant.Quantity both actually decrement.
    ///
    /// ONE-WAY, NOT A LOAN (corrected 2026-08-26 after live testing -- the
    /// original design had this as a Borrow with a due date and a Return flow,
    /// but in practice a transferred unit never comes back, same as how a
    /// picked-up compressor already stays "Picked Up" forever with no return
    /// expected). Only the initial request needs the lending Line's approval
    /// (Engineer+, same Line/Team as the item); once decided, nothing further
    /// happens to this row.
    /// </summary>
    public class TransferRequest
    {
        public int Id { get; set; }

        /// <summary>Business id of the item being transferred, e.g. "CCR-0029". Loose reference, no FK (matches TransactionLog/CompressorUnit style).</summary>
        [Required, MaxLength(30)]
        public string ItemId { get; set; } = "";

        public int Quantity { get; set; }

        /// <summary>
        /// How many of Quantity the requester wants thermocoupled -- motor
        /// items only, same meaning as OrderItem.ThermocoupledCount. 0 for
        /// every non-motor line.
        /// </summary>
        public int ThermocoupledCount { get; set; } = 0;

        /// <summary>
        /// Which physical location to pull from -- a preference, not a lock.
        /// References ItemVariant.Id, same convention as OrderItem.RequestedVariantId
        /// (nullable = no preference / single-location item). If the chosen
        /// variant can't cover Quantity alone by approval time, approval spills
        /// into the item's other active variants the same way a normal pickup does.
        /// </summary>
        public int? RequestedVariantId { get; set; }

        [Required, MaxLength(100)]
        public string RequesterUserName { get; set; } = "";

        /// <summary>"Requested" | "Approved" | "Denied". See TransferStatus.</summary>
        [Required, MaxLength(20)]
        public string Status { get; set; } = TransferStatus.Requested;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? Note { get; set; }

        // ----- Set once decided -----

        [MaxLength(100)]
        public string? DecidedBy { get; set; }
        public DateTime? DecidedAt { get; set; }
    }

    /// <summary>Allowed TransferRequest.Status values. Plain strings, matching the loose-string style used for OrderItem.Status/Delivery.Status.</summary>
    public static class TransferStatus
    {
        public const string Requested = "Requested";
        public const string Approved = "Approved";
        public const string Denied = "Denied";
    }
}
