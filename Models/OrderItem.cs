using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryDevTwo.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        [ForeignKey("Order")]
        public int OrderId { get; set; }
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; }

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
