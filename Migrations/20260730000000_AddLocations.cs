using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                });

            // A name may repeat across levels but not within one under the same
            // owner -- two "Rack A" Subs under different Majors are fine.
            migrationBuilder.CreateIndex(
                name: "IX_Locations_Level_ParentId_Name",
                table: "Locations",
                columns: new[] { "Level", "ParentId", "Name" },
                unique: true);

            // ---- SEED: the twelve names that were in LocationCodec.CanonicalNames ----
            migrationBuilder.Sql(@"
                INSERT INTO Locations (Name, Level, ParentId, IsActive)
                SELECT v.n, 'Parent', NULL, 1 FROM (
                    SELECT 'RD Lab' AS n UNION ALL SELECT 'External Yard'
                    UNION ALL SELECT 'New Test Cells' UNION ALL SELECT 'Plant Test Cells'
                ) v
                WHERE NOT EXISTS (SELECT 1 FROM Locations l WHERE l.Level='Parent' AND l.Name = v.n)");

            migrationBuilder.Sql(@"
                INSERT INTO Locations (Name, Level, ParentId, IsActive)
                SELECT v.n, 'Major', (SELECT Id FROM Locations WHERE Level='Parent' AND Name = v.p), 1
                  FROM (
                    SELECT 'Metrology Mezzanine' AS n, 'RD Lab' AS p
                    UNION ALL SELECT '2-6',          'RD Lab'
                    UNION ALL SELECT 'Connex Area',  'External Yard'
                    UNION ALL SELECT 'Trailer Area', 'External Yard'
                  ) v
                WHERE NOT EXISTS (SELECT 1 FROM Locations l WHERE l.Level='Major' AND l.Name = v.n)");

            migrationBuilder.Sql(@"
                INSERT INTO Locations (Name, Level, ParentId, IsActive)
                SELECT v.n, 'Sub', (SELECT Id FROM Locations WHERE Level='Major' AND Name = v.p), 1
                  FROM (
                    SELECT 'Samurai' AS n,     'Metrology Mezzanine' AS p
                    UNION ALL SELECT 'Ninja',        'Metrology Mezzanine'
                    UNION ALL SELECT 'Connex Box 6', 'Connex Area'
                    UNION ALL SELECT 'Trailer 1',    'Trailer Area'
                  ) v
                WHERE NOT EXISTS (SELECT 1 FROM Locations l WHERE l.Level='Sub' AND l.Name = v.n)");

            // ---- SEED: the six names the old list never had ----
            // 34 ItemVariant rows carry Sub codes MZA3-MZA8. Under the 1/3/5/last
            // rule "Mezz A3" -> M,Z,A,3 = MZA3, so these are the names behind them.
            // ("Mezzanine A3" would encode to MZN3, so it is not that.)
            migrationBuilder.Sql(@"
                INSERT INTO Locations (Name, Level, ParentId, IsActive)
                SELECT v.n, 'Sub', (SELECT Id FROM Locations WHERE Level='Major' AND Name='Metrology Mezzanine'), 1
                  FROM (
                    SELECT 'Mezz A3' AS n UNION ALL SELECT 'Mezz A4' UNION ALL SELECT 'Mezz A5'
                    UNION ALL SELECT 'Mezz A6' UNION ALL SELECT 'Mezz A7' UNION ALL SELECT 'Mezz A8'
                  ) v
                WHERE NOT EXISTS (SELECT 1 FROM Locations l WHERE l.Level='Sub' AND l.Name = v.n)");

            // ---- DATA FIX: the Lean-To was recorded two different ways ----
            // 2 variants had Major='LATO' (encoded "Lean-To"); the 177 loaded on
            // 7/28 use Rack='Lean-To' with no Major, matching the convention
            // CCR-0071..0075 set before any of this. Move the outlier to the
            // convention rather than rewriting 177 rows to match 2.
            //
            // FdaString is rebuilt from the parts so the stored breadcrumb agrees
            // with the columns -- it is a denormalised copy and would otherwise lie.
            migrationBuilder.Sql(@"
                UPDATE ItemVariants
                   SET Major = '',
                       Rack  = 'Lean-To',
                       FdaString = Parent || '.LEAN-TO'
                 WHERE Major = 'LATO'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The Lean-To fold is NOT reversed: after the fact there is no way to
            // tell which Rack='Lean-To' rows were the original two.
            migrationBuilder.DropTable(name: "Locations");
        }
    }
}
