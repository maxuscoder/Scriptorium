using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Scriptorium.Infrastructure;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20260909110000_AddLastPlayedSortKey")]
public partial class AddLastPlayedSortKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "LastPlayedUnixTimeMilliseconds",
            table: "MediaItems",
            type: "INTEGER",
            nullable: true);

        // Existing LastPlayed values are stored as SQLite-compatible ISO timestamps.
        // Convert them to the numeric UTC sort key used by the new queries.
        migrationBuilder.Sql(
            "UPDATE \"MediaItems\" SET \"LastPlayedUnixTimeMilliseconds\" = " +
            "CAST((julianday(\"LastPlayed\") - 2440587.5) * 86400000 AS INTEGER) " +
            "WHERE \"LastPlayed\" IS NOT NULL;");

        migrationBuilder.CreateIndex(
            name: "IX_MediaItems_LastPlayedUnixTimeMilliseconds",
            table: "MediaItems",
            column: "LastPlayedUnixTimeMilliseconds");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_MediaItems_LastPlayedUnixTimeMilliseconds",
            table: "MediaItems");

        migrationBuilder.DropColumn(
            name: "LastPlayedUnixTimeMilliseconds",
            table: "MediaItems");
    }
}
