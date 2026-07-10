using InventoryDevTwo.Models;

namespace InventoryDevTwo.Models
{
    public class SearchResult
    {
        public string Mode { get; set; } = "";
        // "Omni" or "Filter" (debug / UI use)

        public List<InventoryItem> Items { get; set; } = new();

        public bool HasResults => Items.Any();
    }
}