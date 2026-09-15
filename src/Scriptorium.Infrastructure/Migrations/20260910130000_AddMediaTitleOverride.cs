using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260910130000_AddMediaTitleOverride")]
public partial class AddMediaTitleOverride : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TitleOverride",
            table: "MediaItems",
            type: "TEXT",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TitleOverride",
            table: "MediaItems");
    }
}
