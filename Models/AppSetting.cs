using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>Key/value store for app-wide switches. A new setting is a new row, not a migration.</summary>
    public class AppSetting
    {
        public int Id { get; set; }

        /// <summary>e.g. "EnforceRheemPartNumber", "DefaultAlertThreshold".</summary>
        [Required, MaxLength(100)]
        public string Key { get; set; } = "";

        /// <summary>Stored as text; callers parse to bool/int as needed.</summary>
        [MaxLength(500)]
        public string Value { get; set; } = "";

        public DateTime? UpdatedAt { get; set; }

        public string UpdatedBy { get; set; } = "";
    }
}
