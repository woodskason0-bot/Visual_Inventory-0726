using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAndItemLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Line",
                table: "Users",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Line",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Line",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Line",
                table: "InventoryItems");
        }
    }
}
