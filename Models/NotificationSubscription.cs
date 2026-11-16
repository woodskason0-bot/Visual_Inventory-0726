using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// Per-user opt-in for one notification category. Keyed to a real User row and
    /// cascades on delete: current-state config, not an audit record.
    /// </summary>
    public class NotificationSubscription
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        /// <summary>e.g. "PickupRequested". Free-form: a new category is a new row, not a migration.</summary>
        [Required, MaxLength(100)]
        public string Category { get; set; } = "";

        public bool Enabled { get; set; } = true;
    }
}
