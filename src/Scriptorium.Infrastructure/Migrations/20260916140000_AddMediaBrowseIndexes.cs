using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260916140000_AddMediaBrowseIndexes")]
public sealed class AddMediaBrowseIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_MediaItems_MediaType_CategoryId",
            table: "MediaItems",
            columns: new[] { "MediaType", "CategoryId" });
        migrationBuilder.CreateIndex(
            name: "IX_MediaItems_MediaType_DateAdded",
            table: "MediaItems",
            columns: new[] { "MediaType", "DateAdded" });
        migrationBuilder.CreateIndex(
            name: "IX_MediaItems_MediaType_LastPlayedUnixTimeMilliseconds",
            table: "MediaItems",
            columns: new[] { "MediaType", "LastPlayedUnixTimeMilliseconds" });
        migrationBuilder.Sql(
            "CREATE INDEX \"IX_MediaItems_MediaType_DisplayTitle\" ON \"MediaItems\" (\"MediaType\", (CASE WHEN \"TitleOverride\" IS NOT NULL AND trim(\"TitleOverride\") <> '' THEN trim(\"TitleOverride\") ELSE \"Title\" END) COLLATE NOCASE, \"Id\");");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_MediaItems_MediaType_DisplayTitle\";");
        migrationBuilder.DropIndex(
            name: "IX_MediaItems_MediaType_CategoryId",
            table: "MediaItems");
        migrationBuilder.DropIndex(
            name: "IX_MediaItems_MediaType_DateAdded",
            table: "MediaItems");
        migrationBuilder.DropIndex(
            name: "IX_MediaItems_MediaType_LastPlayedUnixTimeMilliseconds",
            table: "MediaItems");
    }
}
