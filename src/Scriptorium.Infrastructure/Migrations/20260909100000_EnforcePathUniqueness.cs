using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Scriptorium.Infrastructure;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260909100000_EnforcePathUniqueness")]
public partial class EnforcePathUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MediaItems_Path\";");
        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX \"IX_MediaItems_Path\" ON \"MediaItems\" (\"Path\" COLLATE NOCASE);");
        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX \"IX_LibraryFolders_Path\" ON \"LibraryFolders\" (\"Path\" COLLATE NOCASE);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_LibraryFolders_Path",
            table: "LibraryFolders");
        migrationBuilder.DropIndex(
            name: "IX_MediaItems_Path",
            table: "MediaItems");
        migrationBuilder.Sql(
            "CREATE INDEX \"IX_MediaItems_Path\" ON \"MediaItems\" (\"Path\");");
    }
}
