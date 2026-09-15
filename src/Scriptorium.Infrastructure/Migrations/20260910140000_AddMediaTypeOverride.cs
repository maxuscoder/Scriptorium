using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260910140000_AddMediaTypeOverride")]
public partial class AddMediaTypeOverride : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int?>(
            name: "DetectedMediaType",
            table: "MediaItems",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int?>(
            name: "MediaTypeOverride",
            table: "MediaItems",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.Sql(
            "UPDATE \"MediaItems\" SET \"DetectedMediaType\" = \"MediaType\" WHERE \"DetectedMediaType\" IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DetectedMediaType", table: "MediaItems");
        migrationBuilder.DropColumn(name: "MediaTypeOverride", table: "MediaItems");
    }
}
