using System;
using System.Collections.Generic;

namespace InventoryDevTwo.Models.ViewModels
{
    // Backs the self-scoped "My Orders" page. Section 1 = this user's order
    // history; Section 2 = their items still out on loan.
    public class MyOrdersViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public List<Order> Orders { get; set; } = new();
        public List<LoanLineViewModel> Loans { get; set; } = new();
    }

    // One still-outstanding loan line. Carries order context (id + when + what)
    // so several near-identical loans stay distinguishable, plus the active
    // locations the returned stock can land in.
    public class LoanLineViewModel
    {
        public int OrderItemId { get; set; }
        public int OrderId { get; set; }
        public DateTime OrderedAt { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public int Outstanding { get; set; }
        public bool ReturnsAsTc { get; set; }   // motor loans come back as TC stock
        public List<VariantChoiceViewModel> LocationChoices { get; set; } = new();
    }
}
