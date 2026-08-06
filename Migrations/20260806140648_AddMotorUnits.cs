using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddMotorUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MotorUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ItemVariantId = table.Column<int>(type: "INTEGER", nullable: true),
                    LabNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OrderId = table.Column<int>(type: "INTEGER", nullable: true),
                    OrderItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    PickedUpAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PickedUpBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MotorUnits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MotorUnits_ItemId",
                table: "MotorUnits",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MotorUnits");
        }
    }
}
