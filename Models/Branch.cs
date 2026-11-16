using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>Top-level org branch, e.g. "Commercial Air". Read through OrgStructure, which caches this table.</summary>
    public class Branch
    {
        public int Id { get; set; }

        /// <summary>e.g. "Commercial Air". Unique among active branches.</summary>
        [Required, MaxLength(60)]
        public string Name { get; set; } = "";

        /// <summary>Soft hide: off the pickers, items and users keep their Lines.</summary>
        public bool IsActive { get; set; } = true;
    }
}
