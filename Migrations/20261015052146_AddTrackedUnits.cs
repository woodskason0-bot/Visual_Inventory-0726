using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackedUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrackedUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ModelNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    PackageType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SerialNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SampleNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Line = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Team = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedUnits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUnits_Line",
                table: "TrackedUnits",
                column: "Line");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrackedUnits");
        }
    }
}
