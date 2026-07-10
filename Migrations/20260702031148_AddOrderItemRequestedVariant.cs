using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryDevTwo.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemRequestedVariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestedVariantId",
                table: "OrderItems",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestedVariantId",
                table: "OrderItems");
        }
    }
}
