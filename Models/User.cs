using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
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

        /// <summary>Access tier: 1 Viewer, 2 Standard, 3 Engineer, 4 Management, 5 Admin.</summary>
        public int AccessLevel { get; set; } = 1;

        /// <summary>
        /// Fixed org placement -- one of the six OrgStructure.AllLines values
        /// (e.g. "Commercial Packaged/Splits"). NULL/blank = not yet assigned,
        /// which fails OPEN (sees everything) rather than closed, so nobody's
        /// dashboard looks broken mid-rollout. Set only from Settings.
        ///
        /// Mutually exclusive with Branch below -- a user has either one specific
        /// Line, or an entire Branch, never both. Line wins if somehow both are
        /// set (shouldn't happen through the UI, which clears the other).
        /// </summary>
        [MaxLength(50)]
        public string? Line { get; set; }

        /// <summary>
        /// One of OrgStructure.BranchLines' keys (e.g. "Commercial Air"). Grants
        /// visibility to every Line under that Branch, for someone who needs
        /// broader-than-one-Line visibility without going all the way to Admin's
        /// "sees everything" bypass (e.g. a director who oversees a whole Branch).
        /// NULL/blank = not set. Only takes effect when Line is also blank.
        /// </summary>
        [MaxLength(50)]
        public string? Branch { get; set; }
    }
}
