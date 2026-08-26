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
        /// "Pending" | "Completed" | "Cancelled" | "Corrected" | "Split". Line-level
        /// state so one short line on a multi-item order can be pulled aside for a
        /// stock correction while its sibling lines pick up normally. A line
        /// that comes up short at pickup is set to "Cancelled" here (its real
        /// fulfillment happens on a freshly issued order against the corrected
        /// count) rather than letting the order close short. OrderService.
        /// ReportShortPull immediately flips a "Cancelled" line to "Corrected"
        /// once it starts acting on it, so a double-submit can't apply the same
        /// stock correction (and reissue a duplicate order) twice. "Split" is a
        /// different situation, deliberately kept separate from "Corrected": the
        /// shelf had enough (or at least the picker never found out otherwise) --
        /// the picker chose to grab what was at hand and defer the rest, which
        /// spins off as a real Pending order (Order.SplitFromOrderId) instead of
        /// leaving the remainder unaccounted for.
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// How many units on THIS order line are requested thermocoupled.
        /// 0 for non-motor lines or when none requested. Capped at TC-available
        /// at add-to-cart time. Rides the order so pickup pulls exactly this
        /// many TC from the source stack.
        /// </summary>
        public int ThermocoupledCount { get; set; } = 0;

        /// <summary>
        /// Returnable units still out on loan for this line (TC motors and
        /// Controls only). 0 = none out, fully returned/scrapped, or a
        /// non-loanable line. Set at pickup; drawn down by Return/Scrap on the
        /// My Orders tab. The line's loan bench disappears when this hits 0.
        /// </summary>
        public int LoanOutstanding { get; set; } = 0;

        // Which physical variant the ENGINEER asked this to be pulled from.
        // Null = "Either location" (the pickup person chooses) or a
        // single-location item where the question never arises. References
        // ItemVariant.Id (the DB PK, stable across renumbering); variants are
        // retired rather than deleted, so old orders always resolve.
        public int? RequestedVariantId { get; set; }

        public virtual Order Order { get; set; } = null!;
    }
}
