using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20261112120000_AddSavedMoments")]
public sealed class AddSavedMoments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SavedMoments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MediaItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                StartSeconds = table.Column<long>(type: "INTEGER", nullable: false),
                EndSeconds = table.Column<long>(type: "INTEGER", nullable: true),
                Title = table.Column<string>(type: "TEXT", nullable: false),
                Notes = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavedMoments", x => x.Id);
                table.CheckConstraint("CK_SavedMoments_TimeRange", "\"StartSeconds\" >= 0 AND (\"EndSeconds\" IS NULL OR \"EndSeconds\" > \"StartSeconds\")");
                table.ForeignKey("FK_SavedMoments_MediaItems_MediaItemId", x => x.MediaItemId, "MediaItems", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_SavedMoments_MediaItemId_StartSeconds", "SavedMoments", new[] { "MediaItemId", "StartSeconds" });
        migrationBuilder.CreateTable(
            name: "SavedMomentTags",
            columns: table => new
            {
                SavedMomentId = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SavedMomentTags", x => new { x.SavedMomentId, x.Name });
                table.ForeignKey("FK_SavedMomentTags_SavedMoments_SavedMomentId", x => x.SavedMomentId, "SavedMoments", "Id", onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SavedMomentTags");
        migrationBuilder.DropTable("SavedMoments");
    }
}
