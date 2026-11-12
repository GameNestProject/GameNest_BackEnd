using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNest_BackEnd.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionCreatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive. The real creation time of existing rows is unknown: they get the migration time,
            // or their review time if already reviewed (a submission is created before it is reviewed).
            // New rows always get CreatedAt from the application; the database default only covers old rows.
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "GameSubmissions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql("""UPDATE "GameSubmissions" SET "CreatedAt" = "ReviewedAt" WHERE "ReviewedAt" IS NOT NULL AND "ReviewedAt" < "CreatedAt";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "GameSubmissions");
        }
    }
}
