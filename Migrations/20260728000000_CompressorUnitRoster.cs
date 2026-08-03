using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class CompressorUnitRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Turns CompressorUnits from a pickup LOG into a unit ROSTER.
            //
            // SQLite cannot relax NOT NULL in place, so the three AlterColumn
            // calls below cause the provider to rebuild the table (create new,
            // copy, drop, rename). That is expected and safe here: the table is
            // empty on the host database and holds 2 test rows on dev.
            //
            // The old index IX_CompressorUnits_ItemId is dropped and recreated
            // by the rebuild automatically -- it is not touched explicitly.

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "CompressorUnits",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PickedUpAt",
                table: "CompressorUnits",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "PickedUpBy",
                table: "CompressorUnits",
                type: "TEXT",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "ItemVariantId",
                table: "CompressorUnits",
                type: "INTEGER",
                nullable: true);

            // Existing rows are all pickups by definition -- the table could not
            // hold anything else before this migration.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "CompressorUnits",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Picked Up");

            migrationBuilder.AddColumn<DateTime>(
                name: "RecordedAt",
                table: "CompressorUnits",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "RecordedBy",
                table: "CompressorUnits",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Backfill the two timestamp/actor pairs for rows that predate the
            // roster, so RecordedAt is never less informative than PickedUpAt.
            migrationBuilder.Sql(@"
                UPDATE CompressorUnits
                   SET RecordedAt = PickedUpAt,
                       RecordedBy = COALESCE(PickedUpBy, '')
                 WHERE PickedUpAt IS NOT NULL");

            // A model may not carry the same serial twice, but two DIFFERENT
            // models may share one -- LG reuses serials across model lines
            // (26E15-#1 appears on both YGH182W and YRH122T in the 7/28 intake).
            // So uniqueness is the PAIR, not the serial. Blank/NULL exempt, same
            // partial-index approach as IX_InventoryItems_RheemPartNumber.
            //
            // NOTE: LabNumber deliberately gets NO uniqueness constraint. Lab
            // numbers are recorded but not yet governed by a process; adding an
            // index now would box in whatever scheme leadership settles on.
            migrationBuilder.CreateIndex(
                name: "IX_CompressorUnits_ItemId_SerialNumber",
                table: "CompressorUnits",
                columns: new[] { "ItemId", "SerialNumber" },
                unique: true,
                filter: "\"SerialNumber\" IS NOT NULL AND \"SerialNumber\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompressorUnits_ItemId_SerialNumber",
                table: "CompressorUnits");

            migrationBuilder.DropColumn(name: "ItemVariantId", table: "CompressorUnits");
            migrationBuilder.DropColumn(name: "Status", table: "CompressorUnits");
            migrationBuilder.DropColumn(name: "RecordedAt", table: "CompressorUnits");
            migrationBuilder.DropColumn(name: "RecordedBy", table: "CompressorUnits");

            // NOTE: reverting past this migration will FAIL if any On Hand units
            // exist, because they have no OrderId and the column becomes NOT NULL
            // again. That is correct -- there is no honest way to invent an order
            // for stock that was counted, not ordered.
            migrationBuilder.AlterColumn<string>(
                name: "PickedUpBy",
                table: "CompressorUnits",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "PickedUpAt",
                table: "CompressorUnits",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "CompressorUnits",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
