using System;
using System.Collections.Generic;
using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Models.ViewModels
{
    // Backs the self-scoped "My Activity" page (renamed 2026-08-26 from "My
    // Orders" once it grew past orders -- loans and internal transfers live
    // here too now). Section 1 = this user's order history; Section 2 = their
    // items still out on loan; Section 3 = internal transfer requests.
    public class MyActivityViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public List<Order> Orders { get; set; } = new();
        public List<LoanLineViewModel> Loans { get; set; } = new();
        public TransferSectionViewModel Transfer { get; set; } = new();
    }

    // One still-outstanding loan line. Carries order context (id + when + what)
    // so several near-identical loans stay distinguishable, plus the active
    // locations the returned stock can land in.
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

        // ----- Pass 6B: compressor disposition -----

        /// <summary>
        /// Compressors are normally consumed, not returned, so their line is
        /// gated behind "Done Using" instead of presenting Return/Scrap up front.
        /// Outstanding here reads as "not yet dispositioned", not "expected back".
        /// </summary>
        public bool IsCompressor { get; set; }

        /// <summary>
        /// The individual units pulled on this line that we actually know by
        /// serial. USUALLY SHORTER THAN Outstanding -- only 184 of 825 compressors
        /// have a serial on record, so a line of 3 may list 1 unit and leave 2
        /// unidentified. The view renders the remainder as a plain quantity,
        /// exactly how motor loans already work.
        /// </summary>
        public List<CompressorUnit> Units { get; set; } = new();

        /// <summary>Outstanding minus the units we can name. Never negative.</summary>
        public int UnidentifiedCount => System.Math.Max(0, Outstanding - Units.Count);
    }
}
