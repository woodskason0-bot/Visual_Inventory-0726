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

        // Engineer's requested pull location (ItemVariant.Id); null = Either.
        // Additive + nullable so pre-existing session drafts deserialize fine.
        public int? RequestedVariantId { get; set; }

        // How many of Quantity the engineer wants to be thermocoupled motors.
        // Session-only (this is a cart DTO, not an EF entity) so it needs no
        // migration; defaults to 0 so old serialized drafts still deserialize.
        public int ThermocoupledCount { get; set; } = 0;
    }
}
