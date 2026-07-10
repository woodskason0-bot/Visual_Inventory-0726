using System.ComponentModel.DataAnnotations;

namespace InventoryDevTwo.Models
{
    /// <summary>
    /// A known team member for the name picker on the Identify page.
    /// This is a convenience roster, not authentication -- it just powers the
    /// dropdown so people pick a consistent "First.Last" instead of free-typing.
    /// </summary>
    public class User
    {
        public int Id { get; set; }

        /// <summary>Login/identifier form, e.g. "Kason.Woods".</summary>
        [Required, MaxLength(100)]
        public string UserName { get; set; } = "";

        /// <summary>Friendly form for display, e.g. "Kason Woods".</summary>
        [MaxLength(100)]
        public string DisplayName { get; set; } = "";

        /// <summary>Per-user UI theme: "dark" or "light". Used by the theme toggle (added later).</summary>
        [MaxLength(10)]
        public string Theme { get; set; } = "dark";

        /// <summary>Soft on/off so someone can be hidden from the picker without losing audit history.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Team this person owns for low-stock notifications, matching InventoryItem.Team
        /// (e.g. "Samurai", "Ninja"). NULL/empty = sees ALL teams' low-stock summary
        /// (managers/admins). Set it only for the supervisors who should be narrowed.
        /// Requires a migration to add the Users.Team column.
        /// </summary>
        [MaxLength(50)]
        public string? Team { get; set; }

        /// <summary>Access tier: 1 Viewer, 2 Standard, 3 Engineer, 4 Management, 5 Admin.</summary>
        public int AccessLevel { get; set; } = 1;
    }
}
