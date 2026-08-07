using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A managed top-level org branch (e.g. "Commercial Air", "Sustaining"),
    /// replacing the hardcoded OrgStructure.BranchLines dictionary. Same
    /// pattern as Team/Location: OrgStructure stays the read path everything
    /// else calls (BranchFor/GroupFor/IsValidLine unchanged), refreshed from
    /// this table at startup and after any Settings edit -- see
    /// OrgStructure.Refresh() and SettingsController.RefreshOrgStructure().
    /// </summary>
    public class Branch
    {
        public int Id { get; set; }

        /// <summary>e.g. "Commercial Air". Unique among ACTIVE branches.</summary>
        [Required, MaxLength(60)]
        public string Name { get; set; } = "";

        /// <summary>Soft hide, same pattern as Team/Location -- drops out of the
        /// pickers without orphaning items/users already carrying one of its Lines.</summary>
        public bool IsActive { get; set; } = true;
    }
}
