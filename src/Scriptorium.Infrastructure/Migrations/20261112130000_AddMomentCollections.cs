using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20261112130000_AddMomentCollections")]
public sealed class AddMomentCollections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("MomentCollections", table => new
        {
            Id = table.Column<Guid>(type: "TEXT", nullable: false),
            Name = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_MomentCollections", x => x.Id));
        migrationBuilder.CreateTable("MomentCollectionMemberships", table => new
        {
            CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
            SavedMomentId = table.Column<Guid>(type: "TEXT", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_MomentCollectionMemberships", x => new { x.CollectionId, x.SavedMomentId });
            table.ForeignKey("FK_MomentCollectionMemberships_MomentCollections_CollectionId", x => x.CollectionId, "MomentCollections", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_MomentCollectionMemberships_SavedMoments_SavedMomentId", x => x.SavedMomentId, "SavedMoments", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex("IX_MomentCollectionMemberships_SavedMomentId", "MomentCollectionMemberships", "SavedMomentId");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("MomentCollectionMemberships");
        migrationBuilder.DropTable("MomentCollections");
    }
}
