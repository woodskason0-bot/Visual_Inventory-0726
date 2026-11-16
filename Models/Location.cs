using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A managed location name. The code is never stored: LocationCodec.Encode
    /// derives it from the name and is the only authority.
    /// </summary>
    public class Location
    {
        public int Id { get; set; }

        /// <summary>Friendly name, e.g. "Metrology Mezzanine". The code is derived from this.</summary>
        [Required, MaxLength(80)]
        public string Name { get; set; } = "";

        /// <summary>"Parent" | "Major" | "Sub". See LocationLevel.</summary>
        [Required, MaxLength(10)]
        public string Level { get; set; } = LocationLevel.Parent;

        /// <summary>Owner one level up. Null for a Parent. A Major sits under a Parent, a Sub under a Major.</summary>
        public int? ParentId { get; set; }

        /// <summary>Soft hide: off the pickers, variants already there are untouched.</summary>
        public bool IsActive { get; set; } = true;
    }

    public static class LocationLevel
    {
        public const string Parent = "Parent";
        public const string Major  = "Major";
        public const string Sub    = "Sub";

        public static readonly string[] All = { Parent, Major, Sub };
    }
}
