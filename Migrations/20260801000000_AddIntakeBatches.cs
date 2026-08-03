using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddIntakeBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntakeBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Line = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Team = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ParentCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MajorCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SubCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Rack = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Row = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    RequestedLocation = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntakeRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeBatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    RheemPartNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeRows_IntakeBatches_IntakeBatchId",
                        column: x => x.IntakeBatchId,
                        principalTable: "IntakeBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeRows_IntakeBatchId",
                table: "IntakeRows",
                column: "IntakeBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeBatches_Status",
                table: "IntakeBatches",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IntakeRows");
            migrationBuilder.DropTable(name: "IntakeBatches");
        }
    }
}
