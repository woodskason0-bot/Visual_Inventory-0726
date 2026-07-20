using Visual_Inventory_System.Models;

namespace Visual_Inventory_System.Models
{
    public class SearchResult
    {
        public string Mode { get; set; } = "";
        // "Omni" or "Filter" (debug / UI use)

        public List<InventoryItem> Items { get; set; } = new();

        public bool HasResults => Items.Any();
    }
}