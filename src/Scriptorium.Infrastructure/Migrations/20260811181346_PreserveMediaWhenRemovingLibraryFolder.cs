using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scriptorium.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreserveMediaWhenRemovingLibraryFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaItems_LibraryFolders_LibraryFolderId",
                table: "MediaItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "LibraryFolderId",
                table: "MediaItems",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaItems_LibraryFolders_LibraryFolderId",
                table: "MediaItems",
                column: "LibraryFolderId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaItems_LibraryFolders_LibraryFolderId",
                table: "MediaItems");

            migrationBuilder.AlterColumn<Guid>(
                name: "LibraryFolderId",
                table: "MediaItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaItems_LibraryFolders_LibraryFolderId",
                table: "MediaItems",
                column: "LibraryFolderId",
                principalTable: "LibraryFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
