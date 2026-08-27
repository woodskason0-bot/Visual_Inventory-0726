using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TransferRequestId",
                table: "MotorUnits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransferRequestId",
                table: "CompressorUnits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TransferRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    ThermocoupledCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedVariantId = table.Column<int>(type: "INTEGER", nullable: true),
                    RequesterUserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DecidedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_ItemId",
                table: "TransferRequests",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_RequesterUserName",
                table: "TransferRequests",
                column: "RequesterUserName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "TransferRequestId",
                table: "MotorUnits");

            migrationBuilder.DropColumn(
                name: "TransferRequestId",
                table: "CompressorUnits");
        }
    }
}
