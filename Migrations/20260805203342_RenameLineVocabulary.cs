using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class RenameLineVocabulary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Line is stored as free text on InventoryItems/Users, not FK'd to
            // OrgStructure.BranchLines -- correcting the constant's spelling
            // without renaming already-stored rows would silently orphan them
            // from Branch resolution (OrgStructure.BranchFor/IsValidLine do an
            // exact string match). No-op for any row not on the old spelling.
            migrationBuilder.Sql("UPDATE InventoryItems SET Line = 'Commercial Packaged/Splits' WHERE Line = 'Commercial Package/Splits';");
            migrationBuilder.Sql("UPDATE InventoryItems SET Line = 'Residential Packaged' WHERE Line = 'Residential Package';");
            migrationBuilder.Sql("UPDATE InventoryItems SET Line = 'Residential Gas Furnaces' WHERE Line = 'Residential Gas Furnace';");
            migrationBuilder.Sql("UPDATE Users SET Line = 'Commercial Packaged/Splits' WHERE Line = 'Commercial Package/Splits';");
            migrationBuilder.Sql("UPDATE Users SET Line = 'Residential Packaged' WHERE Line = 'Residential Package';");
            migrationBuilder.Sql("UPDATE Users SET Line = 'Residential Gas Furnaces' WHERE Line = 'Residential Gas Furnace';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE InventoryItems SET Line = 'Commercial Package/Splits' WHERE Line = 'Commercial Packaged/Splits';");
            migrationBuilder.Sql("UPDATE InventoryItems SET Line = 'Residential Package' WHERE Line = 'Residential Packaged';");
            migrationBuilder.Sql("UPDATE InventoryItems SET Line = 'Residential Gas Furnace' WHERE Line = 'Residential Gas Furnaces';");
            migrationBuilder.Sql("UPDATE Users SET Line = 'Commercial Package/Splits' WHERE Line = 'Commercial Packaged/Splits';");
            migrationBuilder.Sql("UPDATE Users SET Line = 'Residential Package' WHERE Line = 'Residential Packaged';");
            migrationBuilder.Sql("UPDATE Users SET Line = 'Residential Gas Furnace' WHERE Line = 'Residential Gas Furnaces';");
        }
    }
}
