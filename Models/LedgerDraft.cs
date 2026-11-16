namespace Visual_Inventory_System.Models
{
    public class LedgerDraft
    {
        public List<LedgerDraftEntry> Entries { get; set; } = new();

        public void Clear()
        {
            Entries.Clear();
        }
    }

    public class LedgerDraftEntry
    {
        public string ItemId { get; set; } = "";
        public string Action { get; set; } = "";   // "ADD_TO_CART", "RESTOCK", etc.
        public int Quantity { get; set; }

        // Requested pull location (ItemVariant.Id). Null = either.
        public int? RequestedVariantId { get; set; }

        // How many of Quantity should be thermocoupled motors.
        public int ThermocoupledCount { get; set; } = 0;

        // Team to order against. Set only when AddItem had to ask; Submit resolves the rest.
        public string? RequestedTeam { get; set; }
    }
}
