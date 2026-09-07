using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestoreTutorialCourses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LibraryFolders_MediaType",
                table: "LibraryFolders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LibraryFolders_MediaType",
                table: "LibraryFolders",
                sql: "\"MediaType\" IN (0, 1, 2)");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration repairs databases affected by a reverted destructive migration.
            // Rolling it back must not remove tutorial data from otherwise healthy databases.
        }
    }
}
