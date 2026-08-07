using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchesAndLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    BranchId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgLines_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Branches_Name",
                table: "Branches",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgLines_BranchId",
                table: "OrgLines",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgLines_Name",
                table: "OrgLines",
                column: "Name",
                unique: true);

            // Seed the 3 branches / 10 lines that were hardcoded in
            // OrgStructure.BranchLines, so nothing on an already-live system
            // changes meaning the moment this migration applies. Idempotent,
            // same convention as AddTeams.
            migrationBuilder.Sql(@"
                INSERT INTO Branches (Name, IsActive)
                SELECT 'Residential Air', 1
                 WHERE NOT EXISTS (SELECT 1 FROM Branches WHERE Name = 'Residential Air')");
            migrationBuilder.Sql(@"
                INSERT INTO Branches (Name, IsActive)
                SELECT 'Commercial Air', 1
                 WHERE NOT EXISTS (SELECT 1 FROM Branches WHERE Name = 'Commercial Air')");
            migrationBuilder.Sql(@"
                INSERT INTO Branches (Name, IsActive)
                SELECT 'Sustaining', 1
                 WHERE NOT EXISTS (SELECT 1 FROM Branches WHERE Name = 'Sustaining')");

            migrationBuilder.Sql(@"
                INSERT INTO OrgLines (Name, BranchId, IsActive)
                SELECT x.Name, b.Id, 1
                  FROM (SELECT 'Residential OD' AS Name, 'Residential Air' AS Branch
                        UNION ALL SELECT 'Residential Coils/AH', 'Residential Air'
                        UNION ALL SELECT 'Residential Gas Furnaces', 'Residential Air'
                        UNION ALL SELECT 'Commercial Packaged/Splits', 'Commercial Air'
                        UNION ALL SELECT 'Residential Packaged', 'Commercial Air'
                        UNION ALL SELECT 'International', 'Commercial Air'
                        UNION ALL SELECT '944 - International Sustaining - Residential', 'Sustaining'
                        UNION ALL SELECT '949 - Nuevo Laredo Sustaining', 'Sustaining'
                        UNION ALL SELECT '954 - International Sustaining - Commercial', 'Sustaining'
                        UNION ALL SELECT '959 - Fort Smith Sustaining', 'Sustaining') x
                  JOIN Branches b ON b.Name = x.Branch
                 WHERE NOT EXISTS (SELECT 1 FROM OrgLines o WHERE o.Name = x.Name)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgLines");

            migrationBuilder.DropTable(
                name: "Branches");
        }
    }
}
