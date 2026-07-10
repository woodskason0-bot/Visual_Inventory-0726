using System;

namespace InventoryDevTwo.Models
{
    // A lightweight "sticky note" task on the Tasks Available board -- the
    // "Go Store These" column. Supervisors often don't have item details when
    // they post one, so there's deliberately NO structured pre-description:
    // just a title + optional note pointing at the New Item Registry.
    //
    // Claim lifecycle (a corkboard card, not an order):
    //   Open  --(someone claims it)-->  Claimed  --(they finish)-->  Done
    // First to claim locks their name in; everyone else sees "claimed by X".
    // Flat table, no navigations -- nothing for OnModelCreating to configure.
    public class VisTask
    {
        public int Id { get; set; }

        // Only "Store" today; kept as a field so a future task kind slots in
        // without a schema change.
        public string TaskType { get; set; } = "Store";

        public string Title { get; set; } = string.Empty;

        // Optional free-text note. Nullable -- a supervisor may post with none.
        public string? Details { get; set; }

        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Who has it, and when they grabbed it. Null while still Open.
        public string? ClaimedBy { get; set; }
        public DateTime? ClaimedAt { get; set; }

        // "Open" | "Claimed" | "Done".
        public string Status { get; set; } = "Open";

        // When it was marked finished. Null until Done.
        public DateTime? CompletedAt { get; set; }
    }
}
