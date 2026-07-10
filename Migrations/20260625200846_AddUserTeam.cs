using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryDevTwo.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "Users",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Team",
                table: "Users");
        }
    }
}
