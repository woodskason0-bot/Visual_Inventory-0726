using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ProjectCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);

            // Seed the two teams that were hardcoded in the dropdowns, with the
            // project codes the old ternary handed out. Idempotent so a rerun on
            // a database that already has them is a no-op.
            migrationBuilder.Sql(@"
                INSERT INTO Teams (Name, ProjectCode, IsActive)
                SELECT 'Samurai', '7166', 1
                 WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Samurai')");

            migrationBuilder.Sql(@"
                INSERT INTO Teams (Name, ProjectCode, IsActive)
                SELECT 'Ninja', '7165', 1
                 WHERE NOT EXISTS (SELECT 1 FROM Teams WHERE Name = 'Ninja')");

            // Anything else already sitting in InventoryItems.Team gets carried in
            // so the picker never silently drops a value the data is using.
            migrationBuilder.Sql(@"
                INSERT INTO Teams (Name, ProjectCode, IsActive)
                SELECT DISTINCT i.Team, COALESCE(i.ProjectCode, ''), 1
                  FROM InventoryItems i
                 WHERE i.Team IS NOT NULL AND TRIM(i.Team) <> ''
                   AND NOT EXISTS (SELECT 1 FROM Teams t WHERE t.Name = i.Team)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Teams");
        }
    }
}
