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
            // ORDERING IS LOAD-BEARING. The previous version of this migration ran
            // the blank -> "N/A" fold FIRST, while the original index was still
            // live. That index's filter only exempts '', so folding 300+ blank
            // rows to the same "N/A" value collided them all against a uniqueness
            // rule that had no exemption for it yet -- "UNIQUE constraint failed:
            // InventoryItems.RheemPartNumber" on the second row touched.
            //
            // Program.cs wraps db.Database.Migrate() in a try/catch that only logs
            // to console, so the throw was swallowed on every startup. And because
            // EF applies pending migrations in Id order, this one sat at the front
            // of the queue and silently blocked 20260726 (Line) and 20260727
            // (CompressorUnits) behind it -- both of which had to be hand-applied
            // via raw SQL as a result.
            //
            // Drop the old index BEFORE touching data.
            migrationBuilder.DropIndex(
                name: "IX_InventoryItems_RheemPartNumber",
                table: "InventoryItems");

            // Fold legacy blanks into the same "N/A" sentinel used going forward,
            // so there's exactly one flagged state instead of two. One-way: once
            // folded, a blank and an originally-typed "N/A" can't be told apart.
            migrationBuilder.Sql(
                "UPDATE InventoryItems SET RheemPartNumber = 'N/A' WHERE RheemPartNumber = ''");

            // One-time cleanup of pre-sentinel placeholder strings. New-registration
            // hard-requires a non-empty PN (HomeController), so before "N/A" existed
            // as a shareable value the only way past that gate was to invent a
            // unique-looking variant per item. These are those.
            //
            // Deliberately an explicit literal list rather than LIKE 'N/A%' -- a
            // pattern is free to eat a real Rheem part number that happens to start
            // with those characters. Matches zero rows on databases that never had
            // them, so it is safe to run anywhere.
            migrationBuilder.Sql(@"
                UPDATE InventoryItems
                SET RheemPartNumber = 'N/A'
                WHERE RheemPartNumber IN (
                    'N/A - 2',
                    'N/A - 3',
                    'N/A - 4',
                    'N/A - 5',
                    'N/a - Yet',
                    'None yet'
                )");

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

            // NOTE: neither the blank -> "N/A" fold nor the placeholder cleanup
            // above is reversed here. There is no way to tell which "N/A" rows
            // were originally blank, which were placeholders, and which were
            // already "N/A", so Down() restores the index shape but not the old
            // data. Down() is here for shape parity, not as a supported rollback.
        }
    }
}
