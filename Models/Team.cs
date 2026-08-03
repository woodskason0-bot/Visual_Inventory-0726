using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// A managed Team / Project pairing, replacing the hardcoded Samurai/Ninja
    /// options that lived in four dropdowns and one ternary
    /// (ProjectCode = newTeam == "ninja" ? "7165" : "7166" -- which silently gave
    /// every future team Samurai's project code).
    ///
    /// VOCABULARY TABLE, NOT A FOREIGN KEY. InventoryItem.Team and User.Team stay
    /// plain strings, exactly like Line: ad-hoc SQL against the database stays
    /// simple, and an item whose team was later hidden keeps reading sensibly.
    /// The trade is that renaming a team does NOT cascade -- which is why rename
    /// is deliberately not offered yet (see SettingsController).
    ///
    /// Team is now OPTIONAL. Blank / "N/A" is a first-class choice: it is a label
    /// for reporting, not the thing that decides who can see an item. Visibility
    /// belongs to Line (Pass 3) and nothing here touches it.
    /// </summary>
    public class Team
    {
        public int Id { get; set; }

        /// <summary>e.g. "Samurai". Unique among ACTIVE teams; what items store.</summary>
        [Required, MaxLength(60)]
        public string Name { get; set; } = "";

        /// <summary>e.g. "7166". Free text -- some teams may not have one.</summary>
        [MaxLength(30)]
        public string ProjectCode { get; set; } = "";

        /// <summary>
        /// Soft hide, same pattern Pass 1 used for users: drops the team out of
        /// the pickers without orphaning the items that already reference it.
        /// </summary>
        public bool IsActive { get; set; } = true;
    }
}
