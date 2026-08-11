using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryFolderDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "LibraryFolders",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "LibraryFolders");
        }
    }
}
