using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>Team vocabulary, not an FK: items and users store the name. Never gates visibility.</summary>
    public class Team
    {
        public int Id { get; set; }

        /// <summary>e.g. "Samurai". Unique among active teams.</summary>
        [Required, MaxLength(60)]
        public string Name { get; set; } = "";

        /// <summary>e.g. "7166". Free text, optional.</summary>
        [MaxLength(30)]
        public string ProjectCode { get; set; } = "";

        /// <summary>Soft hide: off the pickers, items keep the name.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Home Line, or null. Suggestion only, never enforced.</summary>
        [MaxLength(60)]
        public string? Line { get; set; }
    }
}
