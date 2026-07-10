using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InventoryDevTwo.Migrations
{
    /// <inheritdoc />
    public partial class AddItemRegisteredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAt",
                table: "InventoryItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegisteredAt",
                table: "InventoryItems");
        }
    }
}
