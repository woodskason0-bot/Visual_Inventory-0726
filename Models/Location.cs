using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A managed location NAME. Replaces four hand-maintained copies of the same
    /// vocabulary that had already drifted apart:
    ///
    ///   Services/LocationCodec.cs      CanonicalNames[]   (the decode source)
    ///   Views/Home/Index.cshtml        locMap {}          (the JS cascade)
    ///   Views/Home/Index.cshtml        hardcoded &lt;option value="RLB"&gt;
    ///   Views/Home/MyOrders.cshtml     the same list again
    ///
    /// The drift was real: 34 ItemVariant rows carried Sub codes MZA3-MZA8 that
    /// no copy knew about, so the UI rendered raw codes at every breadcrumb.
    ///
    /// CODES ARE NEVER STORED. LocationCodec.Encode() still derives them from the
    /// name by the same 1st/3rd/5th/last rule -- that rule is untouched and remains
    /// the only authority. Storing a code would create a second one that could
    /// disagree with it.
    ///
    /// Rack and Row stay free text on ItemVariant: they are per-shelf detail, not
    /// shared vocabulary, and the Lean-To lives there by design (Pass 7C option B).
    /// </summary>
    public class Location
    {
        public int Id { get; set; }

        /// <summary>Friendly name, e.g. "Metrology Mezzanine". The code is derived from this.</summary>
        [Required, MaxLength(80)]
        public string Name { get; set; } = "";

        /// <summary>"Parent" | "Major" | "Sub" -- see LocationLevel.</summary>
        [Required, MaxLength(10)]
        public string Level { get; set; } = LocationLevel.Parent;

        /// <summary>
        /// Owning location one level up. NULL for Parent rows. A Major belongs to a
        /// Parent; a Sub belongs to a Major -- which is exactly the shape the JS
        /// cascade needs, so it can be serialized straight out of this table.
        /// </summary>
        public int? ParentId { get; set; }

        /// <summary>Soft hide -- drops it from the pickers without touching variants already there.</summary>
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
