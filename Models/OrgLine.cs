using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A managed Line under a Branch (e.g. "Commercial Packaged/Splits" under
    /// "Commercial Air"), replacing the hardcoded OrgStructure.BranchLines
    /// dictionary. Named OrgLine, not Line, because User.Line/InventoryItem.Line/
    /// Team.Line already exist as plain free-text strings (deliberately not FK'd
    /// to this table -- see Branch.cs and OrgStructure.cs) and a same-named class
    /// would be confusing next to those.
    /// </summary>
    public class OrgLine
    {
        public int Id { get; set; }

        /// <summary>e.g. "Commercial Packaged/Splits". Unique among ACTIVE lines,
        /// across every branch -- User/InventoryItem/Team.Line are flat strings
        /// with no branch qualifier, so two branches can't share a Line name.</summary>
        [Required, MaxLength(60)]
        public string Name { get; set; } = "";

        public int BranchId { get; set; }
        public Branch? Branch { get; set; }

        /// <summary>Soft hide, same pattern as Team/Location.</summary>
        public bool IsActive { get; set; } = true;
    }
}
