using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameNest_BackEnd.Migrations
{
    /// <summary>
    /// Case-insensitive uniqueness of user emails at the database level: a unique index on lower("Email").
    /// The existing case-sensitive IX_Users_Email stays (it is part of the EF model). EF Core cannot model
    /// expression indexes, so this index exists only in SQL and the model snapshot is unchanged.
    /// If case-variant duplicates exist, the migration stops with an explicit error and changes nothing
    /// (PostgreSQL runs the migration in a transaction); resolve the duplicates first (see README).
    /// </summary>
    public partial class AddUsersEmailLowerUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE duplicate_count integer;
                BEGIN
                    SELECT count(*) INTO duplicate_count
                    FROM (SELECT 1 FROM "Users" GROUP BY lower("Email") HAVING count(*) > 1) AS duplicates;
                    IF duplicate_count > 0 THEN
                        RAISE EXCEPTION 'Cannot create IX_Users_Email_Lower: % email address(es) exist in several letter cases. Resolve them first (see README, "Applying EF Core Migrations").', duplicate_count;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql("""CREATE UNIQUE INDEX "IX_Users_Email_Lower" ON "Users" (lower("Email"));""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Users_Email_Lower";""");
        }
    }
}
