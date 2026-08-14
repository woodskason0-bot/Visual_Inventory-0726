using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Deliveries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    TrackingNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BrandOfShipping = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    BrandOfItem = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RecipientUserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LoggedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LoggedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ClaimedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_RecipientUserName",
                table: "Deliveries",
                column: "RecipientUserName");

            migrationBuilder.CreateIndex(
                name: "IX_Deliveries_Status",
                table: "Deliveries",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deliveries");
        }
    }
}
