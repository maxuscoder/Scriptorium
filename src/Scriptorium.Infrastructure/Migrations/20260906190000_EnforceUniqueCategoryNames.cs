using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

/// <inheritdoc />
public partial class EnforceUniqueCategoryNames : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "CREATE UNIQUE INDEX \"IX_Categories_Name\" ON \"Categories\" (\"Name\" COLLATE NOCASE);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Categories_Name",
            table: "Categories");
    }
}
