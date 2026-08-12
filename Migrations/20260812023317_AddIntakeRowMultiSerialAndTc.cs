using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddIntakeRowMultiSerialAndTc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "IntakeRows");

            migrationBuilder.AddColumn<string>(
                name: "Serials",
                table: "IntakeRows",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThermocoupledQty",
                table: "IntakeRows",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Serials",
                table: "IntakeRows");

            migrationBuilder.DropColumn(
                name: "ThermocoupledQty",
                table: "IntakeRows");

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "IntakeRows",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }
    }
}
