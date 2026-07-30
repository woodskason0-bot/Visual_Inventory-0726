using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationZones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    W = table.Column<double>(type: "REAL", nullable: false),
                    H = table.Column<double>(type: "REAL", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationZones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationZones_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationZones_LocationId",
                table: "LocationZones",
                column: "LocationId");

            // Seed the four rectangles that were hardcoded in Index.cshtml,
            // converted from pixels-against-1146x643 to fractions. Same regions,
            // no longer tied to that image's dimensions.
            //
            //   RD Lab            1025,370,1110,520
            //   External Yard      210, 45, 320,365
            //   New Test Cells     975,165,1010,260
            //   Plant Test Cells   975,455,1010,480
            migrationBuilder.Sql(@"
                INSERT INTO LocationZones (LocationId, X, Y, W, H, Label)
                SELECT l.Id, v.x, v.y, v.w, v.h, NULL
                  FROM (
                    SELECT 'RD Lab'           AS n, 0.894415 AS x, 0.575428 AS y, 0.074171 AS w, 0.233281 AS h
                    UNION ALL SELECT 'External Yard',    0.183246, 0.069984, 0.095986, 0.497667
                    UNION ALL SELECT 'New Test Cells',   0.850785, 0.256610, 0.030541, 0.147745
                    UNION ALL SELECT 'Plant Test Cells', 0.850785, 0.707621, 0.030541, 0.038880
                  ) v
                  JOIN Locations l ON l.Name = v.n AND l.Level = 'Parent'
                 WHERE NOT EXISTS (
                    SELECT 1 FROM LocationZones z WHERE z.LocationId = l.Id)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "LocationZones");
        }
    }
}
