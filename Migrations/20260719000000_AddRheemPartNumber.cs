using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddRheemPartNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RheemPartNumber",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Unique only among NON-blank values: legacy rows without a PN
            // coexist; two items can never share a real Rheem part number.
            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_RheemPartNumber",
                table: "InventoryItems",
                column: "RheemPartNumber",
                unique: true,
                filter: "\"RheemPartNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_RheemPartNumber",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "RheemPartNumber",
                table: "InventoryItems");
        }
    }
}
