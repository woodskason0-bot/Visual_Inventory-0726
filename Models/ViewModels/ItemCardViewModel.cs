using System.Collections.Generic;

namespace Visual_Inventory_System.Models.ViewModels
{
    /// <summary>
    /// Everything the Item Card modal (Pass 40) shows about one item, gathered
    /// server-side in one place so the card never re-derives a number the rest
    /// of the app already has a definition for.
    /// </summary>
    public class ItemCardViewModel
    {
        public InventoryItem Item { get; set; } = null!;

        /// <summary>Branch derived from the item's Line, or "" when unassigned.</summary>
        public string Branch { get; set; } = "";

        /// <summary>Same IsOwnLine rule the search row uses -- decides order vs. request-transfer.</summary>
        public bool IsOwnLine { get; set; }

        public int OnHand { get; set; }

        /// <summary>What this viewer could order right now (team-scoped, pending orders subtracted).</summary>
        public int AvailableToViewer { get; set; }

        /// <summary>Units on lines of still-Pending orders -- already promised, not yet pulled.</summary>
        public int CommittedToPending { get; set; }

        /// <summary>Picked up on a loanable line and not yet returned or scrapped.</summary>
        public int OutOnLoan { get; set; }

        public bool IsLoanable { get; set; }
        public bool IsCompressor { get; set; }
        public bool IsMotor { get; set; }

        /// <summary>Sum of this viewer's cart lines for the item (0 = not in cart).</summary>
        public int InCartQty { get; set; }

        public List<CompressorUnit> CompressorUnits { get; set; } = new();
        public List<MotorUnit> MotorUnits { get; set; } = new();

        /// <summary>Last few log rows for the item, through ApplyLogVisibility.</summary>
        public List<TransactionLog> RecentLogs { get; set; } = new();
    }
}
