using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>
    /// Many-to-many membership row: one user, one team. Replaces the old single
    /// User.Team string -- a user can now belong to several teams at once.
    ///
    /// TeamName is a plain string, not a foreign key to Team.Id -- same
    /// vocabulary-table convention as InventoryItem.Team/Line everywhere else in
    /// this app (see Team.cs). Managed from the team-centric picker in Settings
    /// (pick a team, check/uncheck the users who belong to it), not from the user
    /// row.
    /// </summary>
    public class UserTeam
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [Required, MaxLength(60)]
        public string TeamName { get; set; } = "";
    }
}
