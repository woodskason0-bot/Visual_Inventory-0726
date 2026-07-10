using InventoryDevTwo.Models;

namespace InventoryDevTwo.Models
{
    public class LedgerViewModel
    {
        public List<LedgerDraftEntry> Entries { get; set; } = new();
    }
}