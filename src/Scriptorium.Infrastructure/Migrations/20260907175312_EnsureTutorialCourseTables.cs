using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnsureTutorialCourseTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "Courses" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Courses" PRIMARY KEY,
                    "LibraryFolderId" TEXT NOT NULL,
                    "Title" TEXT NOT NULL,
                    CONSTRAINT "FK_Courses_LibraryFolders_LibraryFolderId"
                        FOREIGN KEY ("LibraryFolderId") REFERENCES "LibraryFolders" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "Lessons" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_Lessons" PRIMARY KEY,
                    "CourseId" TEXT NOT NULL,
                    "MediaItemId" TEXT NOT NULL,
                    "LessonNumber" INTEGER NULL,
                    "SortOrder" INTEGER NOT NULL,
                    "Title" TEXT NOT NULL,
                    "FilePath" TEXT NOT NULL,
                    CONSTRAINT "FK_Lessons_Courses_CourseId"
                        FOREIGN KEY ("CourseId") REFERENCES "Courses" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_Lessons_MediaItems_MediaItemId"
                        FOREIGN KEY ("MediaItemId") REFERENCES "MediaItems" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Courses_LibraryFolderId\" ON \"Courses\" (\"LibraryFolderId\");");
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS \"IX_Lessons_CourseId_SortOrder\" ON \"Lessons\" (\"CourseId\", \"SortOrder\");");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Lessons_MediaItemId\" ON \"Lessons\" (\"MediaItemId\");");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration repairs databases affected by a reverted destructive migration.
            // Rolling it back must not remove tutorial data from otherwise healthy databases.
        }
    }
}
