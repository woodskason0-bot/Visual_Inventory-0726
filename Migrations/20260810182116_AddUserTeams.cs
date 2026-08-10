using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visual_Inventory_System.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamName = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTeams_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTeams_UserId_TeamName",
                table: "UserTeams",
                columns: new[] { "UserId", "TeamName" },
                unique: true);

            // Carry every existing single-Team assignment into the new
            // many-to-many table before the old column is dropped below, so
            // nobody's existing team goes missing.
            migrationBuilder.Sql(
                "INSERT INTO UserTeams (UserId, TeamName) " +
                "SELECT Id, Team FROM Users WHERE Team IS NOT NULL AND TRIM(Team) <> '';");

            migrationBuilder.DropColumn(
                name: "Team",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Team",
                table: "Users",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            // Best-effort restore: a user on multiple teams can only carry one
            // string backward, so this picks one deterministically rather than
            // leaving it blank. Multi-team membership itself is lost on downgrade.
            migrationBuilder.Sql(
                "UPDATE Users SET Team = (" +
                "  SELECT TeamName FROM UserTeams WHERE UserTeams.UserId = Users.Id " +
                "  ORDER BY TeamName LIMIT 1" +
                ");");

            migrationBuilder.DropTable(
                name: "UserTeams");
        }
    }
}
