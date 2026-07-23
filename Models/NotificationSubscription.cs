using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// Per-user opt-in for a named notification category (e.g. "PickupRequested").
    /// Unlike TransactionLog/Order/VisTask -- which snapshot names as plain
    /// strings so history survives a deleted user -- this is keyed to a real
    /// User row and cascades on delete, because it's current-state config,
    /// not an audit record. New categories are new rows here, never a new
    /// migration.
    /// </summary>
    public class NotificationSubscription
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        /// <summary>e.g. "PickupRequested". Free-form so future categories don't need schema changes.</summary>
        [Required, MaxLength(100)]
        public string Category { get; set; } = "";

        public bool Enabled { get; set; } = true;
    }
}
