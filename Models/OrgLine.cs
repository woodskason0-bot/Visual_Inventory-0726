using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A Line under a Branch, e.g. "Commercial Packaged/Splits" under "Commercial Air".
    /// Named OrgLine because Line is already a plain string on User, InventoryItem and Team.
    /// </summary>
    public class OrgLine
    {
        public int Id { get; set; }

        /// <summary>Unique among active lines across every branch: Line strings carry no branch.</summary>
        [Required, MaxLength(60)]
        public string Name { get; set; } = "";

        public int BranchId { get; set; }
        public Branch? Branch { get; set; }

        /// <summary>Soft hide, same pattern as Team/Location.</summary>
        public bool IsActive { get; set; } = true;
    }
}
