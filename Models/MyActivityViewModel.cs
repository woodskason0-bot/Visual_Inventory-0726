using System;
using System.Collections.Generic;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Models.ViewModels
{
    // My Activity page: this user's orders, their loans still out, their transfer requests.
    public class MyActivityViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public List<Order> Orders { get; set; } = new();
        public List<LoanLineViewModel> Loans { get; set; } = new();
        public TransferSectionViewModel Transfer { get; set; } = new();
    }

    // One loan line still out, with enough order context to tell near-identical loans apart.
    public class LoanLineViewModel
    {
        public int OrderItemId { get; set; }
        public int OrderId { get; set; }
        public DateTime OrderedAt { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        // Rheem PN for label-verification at return time. "" = not captured.
        public string RheemPartNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public int Outstanding { get; set; }
        public bool ReturnsAsTc { get; set; }   // motor loans come back as TC stock
        public List<VariantChoiceViewModel> LocationChoices { get; set; } = new();

        // Compressor disposition

        /// <summary>Compressor line: gated behind "Done Using". Outstanding = not yet dispositioned.</summary>
        public bool IsCompressor { get; set; }

        /// <summary>Units on this line known by serial. Usually fewer than Outstanding.</summary>
        public List<CompressorUnit> Units { get; set; } = new();

        /// <summary>Outstanding minus the units we can name. Never negative.</summary>
        public int UnidentifiedCount => System.Math.Max(0, Outstanding - Units.Count);
    }
}
