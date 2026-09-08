using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Scriptorium.Infrastructure;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ScriptoriumDbContext))]
    [Migration("20260909090000_PreserveManualTvShowGrouping")]
    public partial class PreserveManualTvShowGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int?>(
                name: "DetectedEpisodeNumber",
                table: "MediaItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "DetectedSeasonNumber",
                table: "MediaItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedTVShowTitle",
                table: "MediaItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "EpisodeNumberOverride",
                table: "MediaItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int?>(
                name: "SeasonNumberOverride",
                table: "MediaItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TVShowTitleOverride",
                table: "MediaItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"MediaItems\" " +
                "SET \"DetectedTVShowTitle\" = \"TVShowTitle\", " +
                "\"DetectedSeasonNumber\" = \"SeasonNumber\", " +
                "\"DetectedEpisodeNumber\" = \"EpisodeNumber\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedEpisodeNumber",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "DetectedSeasonNumber",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "DetectedTVShowTitle",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "EpisodeNumberOverride",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "SeasonNumberOverride",
                table: "MediaItems");

            migrationBuilder.DropColumn(
                name: "TVShowTitleOverride",
                table: "MediaItems");
        }
    }
}
