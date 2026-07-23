using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AllowNARheemPartNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fold legacy blanks into the same "N/A" sentinel used going forward,
            // so there's exactly one flagged state instead of two. One-way: once
            // folded, a blank and an originally-typed "N/A" can't be told apart.
            migrationBuilder.Sql("UPDATE InventoryItems SET RheemPartNumber = 'N/A' WHERE RheemPartNumber = ''");

            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_RheemPartNumber",
                table: "InventoryItems");

            // "N/A" is now exempt from the uniqueness rule the same way blank
            // already was -- multiple items can share it without "N/A - 2" hacks.
            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_RheemPartNumber",
                table: "InventoryItems",
                column: "RheemPartNumber",
                unique: true,
                filter: "\"RheemPartNumber\" <> '' AND \"RheemPartNumber\" <> 'N/A'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_RheemPartNumber",
                table: "InventoryItems");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryItems_RheemPartNumber",
                table: "InventoryItems",
                column: "RheemPartNumber",
                unique: true,
                filter: "\"RheemPartNumber\" <> ''");

            // NOTE: the blank -> "N/A" fold above is NOT reversed here. There is
            // no way to tell which "N/A" rows were originally blank vs. already
            // "N/A", so Down() restores the index shape but not the old data.
        }
    }
}
