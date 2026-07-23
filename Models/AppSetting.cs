using System;
using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// Generic key/value store for app-wide toggles and config that used to
    /// mean opening the SQLite file directly (see AccessLevels.cs: "editing
    /// a person's tier is a one-number change in the Users table"). Add a new
    /// setting by adding a row from the Settings page -- it never needs a new
    /// migration just to add one more switch.
    /// </summary>
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
