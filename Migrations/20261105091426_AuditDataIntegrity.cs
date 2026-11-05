using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNest_BackEnd.Migrations
{
    /// <inheritdoc />
    public partial class AuditDataIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserGames_Games_GameId",
                table: "UserGames");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UserGames_Status",
                table: "UserGames",
                sql: "\"Status\" IN ('Planned', 'Playing', 'Completed', 'Dropped', 'OnHold')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GameSubmissions_Status",
                table: "GameSubmissions",
                sql: "\"Status\" IN ('Pending', 'Approved', 'Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_GameSubmissions_Type",
                table: "GameSubmissions",
                sql: "\"Type\" IN ('NewGame', 'Correction')");

            migrationBuilder.AddForeignKey(
                name: "FK_UserGames_Games_GameId",
                table: "UserGames",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserGames_Games_GameId",
                table: "UserGames");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UserGames_Status",
                table: "UserGames");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GameSubmissions_Status",
                table: "GameSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_GameSubmissions_Type",
                table: "GameSubmissions");

            migrationBuilder.AddForeignKey(
                name: "FK_UserGames_Games_GameId",
                table: "UserGames",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
