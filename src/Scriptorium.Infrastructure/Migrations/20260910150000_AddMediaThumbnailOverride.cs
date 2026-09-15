using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260910150000_AddMediaThumbnailOverride")]
public partial class AddMediaThumbnailOverride : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DetectedThumbnailPath",
            table: "MediaItems",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ThumbnailOverride",
            table: "MediaItems",
            type: "TEXT",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"MediaItems\" SET \"DetectedThumbnailPath\" = \"ThumbnailPath\" WHERE \"DetectedThumbnailPath\" IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DetectedThumbnailPath", table: "MediaItems");
        migrationBuilder.DropColumn(name: "ThumbnailOverride", table: "MediaItems");
    }
}
