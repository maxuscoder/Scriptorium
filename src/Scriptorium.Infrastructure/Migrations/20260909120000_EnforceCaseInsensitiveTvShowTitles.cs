using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Scriptorium.Infrastructure;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260909120000_EnforceCaseInsensitiveTvShowTitles")]
public partial class EnforceCaseInsensitiveTvShowTitles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_TVShows_LibraryFolderId_Title\";");
        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX \"IX_TVShows_LibraryFolderId_Title\" " +
            "ON \"TVShows\" (\"LibraryFolderId\", \"Title\" COLLATE NOCASE);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_TVShows_LibraryFolderId_Title\";");
        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX \"IX_TVShows_LibraryFolderId_Title\" " +
            "ON \"TVShows\" (\"LibraryFolderId\", \"Title\");");
    }
}
