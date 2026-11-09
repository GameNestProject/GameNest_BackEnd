using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GameNest_BackEnd.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameTitles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameTitles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    EmailConfirmationTokenHash = table.Column<string>(type: "text", nullable: true),
                    EmailConfirmationTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsProfilePublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsLibraryPublic = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CoverUrl = table.Column<string>(type: "text", nullable: false),
                    TrailerUrl = table.Column<string>(type: "text", nullable: false),
                    Developer = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "text", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GameTitleId = table.Column<int>(type: "integer", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Games_GameTitles_GameTitleId",
                        column: x => x.GameTitleId,
                        principalTable: "GameTitles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserCreatedGames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CoverUrl = table.Column<string>(type: "text", nullable: false),
                    Developer = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "text", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCreatedGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserCreatedGames_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GameSubmissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubmittedByUserId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CoverUrl = table.Column<string>(type: "text", nullable: false),
                    TrailerUrl = table.Column<string>(type: "text", nullable: false),
                    Developer = table.Column<string>(type: "text", nullable: false),
                    Publisher = table.Column<string>(type: "text", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AdminComment = table.Column<string>(type: "text", nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameSubmissions", x => x.Id);
                    table.CheckConstraint("CK_GameSubmissions_Status", "\"Status\" IN ('Pending', 'Approved', 'Rejected')");
                    table.CheckConstraint("CK_GameSubmissions_Type", "\"Type\" IN ('NewGame', 'Correction')");
                    table.ForeignKey(
                        name: "FK_GameSubmissions_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GameSubmissions_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GameSubmissions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserGames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    GameId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Purchased = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGames", x => x.Id);
                    table.CheckConstraint("CK_UserGames_Status", "\"Status\" IN ('Planned', 'Playing', 'Completed', 'Dropped', 'OnHold')");
                    table.ForeignKey(
                        name: "FK_UserGames_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserGames_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_GameTitleId",
                table: "Games",
                column: "GameTitleId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubmissions_GameId",
                table: "GameSubmissions",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubmissions_ReviewedByUserId",
                table: "GameSubmissions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameSubmissions_SubmittedByUserId",
                table: "GameSubmissions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GameTitles_Title",
                table: "GameTitles",
                column: "Title",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserCreatedGames_UserId",
                table: "UserCreatedGames",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGames_GameId",
                table: "UserGames",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGames_UserId_GameId",
                table: "UserGames",
                columns: new[] { "UserId", "GameId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameSubmissions");

            migrationBuilder.DropTable(
                name: "UserCreatedGames");

            migrationBuilder.DropTable(
                name: "UserGames");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "GameTitles");
        }
    }
}
