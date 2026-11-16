using System.ComponentModel.DataAnnotations.Schema;

namespace Visual_Inventory_System.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        [ForeignKey("Order")]
        public int OrderId { get; set; }
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; }

        /// <summary>
        /// "Pending" | "Completed" | "Cancelled" | "Corrected" | "Split" | "Waiting" | "Resubmitted" | "Declined".
        /// Per line, not per order. Waiting = TC asked, none on the shelves, the orderer is asked about a Non-TC Motor.
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>What happened to this line, in sentences, oldest first. Null = nothing to say.</summary>
        public string? Note { get; set; }

        /// <summary>Units requested thermocoupled. 0 for non-motors.</summary>
        public int ThermocoupledCount { get; set; } = 0;

        /// <summary>Units still out on loan (TC motors, Controls). 0 = none.</summary>
        public int LoanOutstanding { get; set; } = 0;

        /// <summary>Requested stack (ItemVariant.Id). Null = either location.</summary>
        public int? RequestedVariantId { get; set; }

        /// <summary>Team ordered against, set at Submit. "" = no split, fails open.</summary>
        public string Team { get; set; } = "";

        public virtual Order Order { get; set; } = null!;
    }
}
