using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryDevTwo.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeProjectColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProjectName",
                table: "InventoryItems",
                newName: "Team");

            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Group",
                table: "InventoryItems");

            migrationBuilder.RenameColumn(
                name: "Team",
                table: "InventoryItems",
                newName: "ProjectName");
        }
    }
}
