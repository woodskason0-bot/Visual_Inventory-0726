using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddPerTeamQuantityOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "OrderItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "ItemVariants",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Backfill: every existing variant inherits its parent item's
            // family-level Team, so every pre-existing single-team item keeps
            // behaving exactly as it always has -- "full stack," one team, zero
            // migration risk. OrderItems.Team is deliberately NOT backfilled --
            // a blank Team on a historical order line is accurate (the concept
            // didn't exist when it was placed), not a gap to fill.
            migrationBuilder.Sql(@"
                UPDATE ItemVariants
                SET Team = (SELECT i.Team FROM InventoryItems i WHERE i.Id = ItemVariants.InventoryItemId)
                WHERE Team = '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Team",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Team",
                table: "ItemVariants");
        }
    }
}
