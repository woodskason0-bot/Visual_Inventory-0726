using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailRoutingAndThresholds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ====================================================================
            // COMMENTED OUT: Column 'AlertThreshold' was manually added in SQLite.
            // If EF Core runs this, it throws SQLite Error 1: duplicate column.
            // ====================================================================
            /*
            migrationBuilder.AddColumn<int>(
                name: "AlertThreshold",
                table: "InventoryItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
            */

            // EF Core will ONLY execute this part and create the new table
            migrationBuilder.CreateTable(
                name: "EmailRoutings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Group = table.Column<string>(type: "TEXT", nullable: false),
                    Team = table.Column<string>(type: "TEXT", nullable: false),
                    ManagerName = table.Column<string>(type: "TEXT", nullable: false),
                    ManagerEmail = table.Column<string>(type: "TEXT", nullable: false),
                    SupervisorName = table.Column<string>(type: "TEXT", nullable: false),
                    SupervisorEmail = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailRoutings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailRoutings");

            // ====================================================================
            // COMMENTED OUT: To maintain symmetry, since we didn't add it in 'Up',
            // we shouldn't drop it in 'Down'. 
            // ====================================================================
            /*
            migrationBuilder.DropColumn(
                name: "AlertThreshold",
                table: "InventoryItems");
            */
        }
    }
}