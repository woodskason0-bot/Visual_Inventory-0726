using System;

namespace Visual_Inventory_System.Models
{
    // A sticky-note task in the "Go Store These" column. Title plus optional note, nothing structured.
    // Open -> Claimed -> Done. First claim locks the name in.
    public class VisTask
    {
        public int Id { get; set; }

        // Only "Store" today.
        public string TaskType { get; set; } = "Store";

        public string Title { get; set; } = string.Empty;

        // Optional free-text note.
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
