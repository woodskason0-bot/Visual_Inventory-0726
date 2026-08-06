using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Line",
                table: "Teams",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            // Seed the real Team -> home Line mappings (per Kason, 2026-08-05).
            // Matched by Name, so this is a no-op for any team that doesn't
            // exist yet under that exact name rather than an error.
            migrationBuilder.Sql("UPDATE Teams SET Line = 'Residential OD' WHERE Name = 'Falcon';");
            migrationBuilder.Sql("UPDATE Teams SET Line = 'Residential OD' WHERE Name = 'Polaris';");
            migrationBuilder.Sql("UPDATE Teams SET Line = 'Residential Coils/AH' WHERE Name = 'Hurricane';");
            migrationBuilder.Sql("UPDATE Teams SET Line = 'Residential Packaged' WHERE Name = 'T-Rex';");
            migrationBuilder.Sql("UPDATE Teams SET Line = 'Commercial Packaged/Splits' WHERE Name = 'Spartan';");
            migrationBuilder.Sql("UPDATE Teams SET Line = 'Commercial Packaged/Splits' WHERE Name = 'Ninja';");
            migrationBuilder.Sql("UPDATE Teams SET Line = 'Commercial Packaged/Splits' WHERE Name = 'Samurai';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Line",
                table: "Teams");
        }
    }
}
