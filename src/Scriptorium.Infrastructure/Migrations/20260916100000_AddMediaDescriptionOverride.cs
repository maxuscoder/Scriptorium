using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260916100000_AddMediaDescriptionOverride")]
public partial class AddMediaDescriptionOverride : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DescriptionOverride",
            table: "MediaItems",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DescriptionOverride", table: "MediaItems");
    }
}
