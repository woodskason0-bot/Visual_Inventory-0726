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

        /// <summary>"Pending" | "Completed" | "Cancelled" | "Corrected" | "Split". Per line, not per order.</summary>
        public string Status { get; set; } = "Pending";

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
