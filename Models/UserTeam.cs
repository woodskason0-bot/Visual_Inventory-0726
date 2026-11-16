using System.ComponentModel.DataAnnotations;

namespace Visual_Inventory_System.Models
{
    /// <summary>One user on one team. A user can be on several. TeamName is a plain string, not an FK.</summary>
    public class UserTeam
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [Required, MaxLength(60)]
        public string TeamName { get; set; } = "";
    }
}
