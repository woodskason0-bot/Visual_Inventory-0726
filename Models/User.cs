using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>A roster entry for the Identify name picker. Not authentication.</summary>
    public class User
    {
        public int Id { get; set; }

        /// <summary>Login/identifier form, e.g. "Kason.Woods".</summary>
        [Required, MaxLength(100)]
        public string UserName { get; set; } = "";

        /// <summary>Friendly form for display, e.g. "Kason Woods".</summary>
        [MaxLength(100)]
        public string DisplayName { get; set; } = "";

        /// <summary>"dark" or "light".</summary>
        [MaxLength(10)]
        public string Theme { get; set; } = "dark";

        /// <summary>Soft hide: off the picker, audit history kept.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Access tier: 1 Viewer, 2 Standard, 3 Engineer, 4 Management, 5 Admin.</summary>
        public int AccessLevel { get; set; } = 1;

        /// <summary>
        /// One of OrgStructure.AllLines. Blank = unassigned, which fails open (sees everything).
        /// A user has a Line or a Branch, never both. Line wins if both are set.
        /// </summary>
        [MaxLength(50)]
        public string? Line { get; set; }

        /// <summary>A branch name: sees every Line under it. Only read when Line is blank.</summary>
        [MaxLength(50)]
        public string? Branch { get; set; }
    }
}
