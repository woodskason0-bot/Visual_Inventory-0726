using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Models
{
    public class LedgerViewModel
    {
        public List<LedgerDraftEntry> Entries { get; set; } = new();
    }
}