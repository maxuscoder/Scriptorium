using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260916110000_AddMediaReleaseYearOverride")]
public partial class AddMediaReleaseYearOverride : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ReleaseYearOverride",
            table: "MediaItems",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReleaseYearOverride", table: "MediaItems");
    }
}
