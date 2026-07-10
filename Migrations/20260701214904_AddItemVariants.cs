using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryDevTwo.Migrations
{
    /// <inheritdoc />
    public partial class AddItemVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        { // 1. Build the new apartment first.
          migrationBuilder.CreateTable(
              name: "ItemVariants",
              columns: table => new 
              {
                  Id = table.Column<int>(type: "INTEGER", nullable: false) 
                  .Annotation("Sqlite:Autoincrement", true),
                  InventoryItemId = table.Column<int>(type: "INTEGER", nullable: false),
                  VariantNumber = table.Column<int>(type: "INTEGER", nullable: false),
                  Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                  Parent = table.Column<string>(type: "TEXT", nullable: false),
                  Major = table.Column<string>(type: "TEXT", nullable: false),
                  Sub = table.Column<string>(type: "TEXT", nullable: false),
                  Rack = table.Column<string>(type: "TEXT", nullable: false),
                  Row = table.Column<string>(type: "TEXT", nullable: false),
                  FdaString = table.Column<string>(type: "TEXT", nullable: false),
                  RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                  IsRetired = table.Column<bool>(type: "INTEGER", nullable: false) },
              constraints: table =>
              {
                  table.PrimaryKey("PK_ItemVariants", x => x.Id);
                  table.ForeignKey(
                      name: "FK_ItemVariants_InventoryItems_InventoryItemId",
                      column: x => x.InventoryItemId,
                      principalTable: "InventoryItems",
                      principalColumn: "Id",
                      onDelete: ReferentialAction.Cascade);
              });
            migrationBuilder.CreateIndex(
                name: "IX_ItemVariants_InventoryItemId_VariantNumber",
                table: "ItemVariants",
                columns: new[] { "InventoryItemId", "VariantNumber" });
            // 2. Move everything in while the old columns still exist: // one variant (#1) per item, copying its location/qty/FDA straight across.
            migrationBuilder.Sql(@"
                INSERT INTO ItemVariants (InventoryItemId, VariantNumber, Quantity, Parent, Major, Sub, Rack, Row, FdaString, RegisteredAt, IsRetired)
                SELECT Id, 1, Quantity,
                    IFNULL(Parent,''), IFNULL(Major,''), IFNULL(Sub,''),
                    IFNULL(Rack,''), IFNULL(Row,''), IFNULL(FdaString,''),
                    RegisteredAt, 0
                FROM InventoryItems; ");
            // 3. NOW the old building can come down.
            migrationBuilder.DropColumn(name: "FdaString", table: "InventoryItems");
            migrationBuilder.DropColumn(name: "Major", table: "InventoryItems");
            migrationBuilder.DropColumn(name: "Parent", table: "InventoryItems");
            migrationBuilder.DropColumn(name: "Quantity", table: "InventoryItems");
            migrationBuilder.DropColumn(name: "Rack", table: "InventoryItems");
            migrationBuilder.DropColumn(name: "Row", table: "InventoryItems");
            migrationBuilder.DropColumn(name: "Sub", table: "InventoryItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemVariants");

            migrationBuilder.AddColumn<string>(
                name: "FdaString",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Major",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Parent",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "InventoryItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Rack",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Row",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Sub",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
