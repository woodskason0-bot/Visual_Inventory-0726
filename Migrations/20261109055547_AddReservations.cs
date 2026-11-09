using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReservationId",
                table: "MotorUnits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReservationId",
                table: "CompressorUnits",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReservedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    StartDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EndNote = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    EndingNoticeSent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReservationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReservationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemVariantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    TcQuantity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservationLines_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MotorUnits_ReservationId",
                table: "MotorUnits",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_CompressorUnits_ReservationId",
                table: "CompressorUnits",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLines_ItemVariantId",
                table: "ReservationLines",
                column: "ItemVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLines_ReservationId",
                table: "ReservationLines",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ItemId",
                table: "Reservations",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ReservedBy",
                table: "Reservations",
                column: "ReservedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status",
                table: "Reservations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationLines");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_MotorUnits_ReservationId",
                table: "MotorUnits");

            migrationBuilder.DropIndex(
                name: "IX_CompressorUnits_ReservationId",
                table: "CompressorUnits");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "MotorUnits");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "CompressorUnits");
        }
    }
}
