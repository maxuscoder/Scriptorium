using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Scriptorium.Infrastructure.Migrations;

[DbContext(typeof(ScriptoriumDbContext))]
[Migration("20261112140000_AddPrivateLibrary")]
public sealed class AddPrivateLibrary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("PrivacyPathHash", "MediaItems", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.Sql("""
            CREATE TABLE PrivateLibraryConfigurations (Id INTEGER NOT NULL PRIMARY KEY, Mode INTEGER NOT NULL, AutoLockMinutes INTEGER NOT NULL, Salt BLOB NOT NULL, WrappedKey BLOB NOT NULL);
            CREATE TABLE PrivateMediaRecords (Id TEXT NOT NULL PRIMARY KEY, Payload BLOB NOT NULL);
            CREATE TABLE PrivateSources (PathHash TEXT NOT NULL PRIMARY KEY, PrivateMediaId TEXT NOT NULL);
            CREATE TRIGGER PrivateMediaInsertGuard BEFORE INSERT ON MediaItems
            WHEN EXISTS (SELECT 1 FROM PrivateSources WHERE PathHash = NEW.PrivacyPathHash)
            BEGIN SELECT RAISE(ABORT, 'Private source cannot be indexed publicly'); END;
            CREATE TRIGGER PrivateMediaUpdateGuard BEFORE UPDATE ON MediaItems
            WHEN EXISTS (SELECT 1 FROM PrivateSources WHERE PathHash = NEW.PrivacyPathHash)
            BEGIN SELECT RAISE(ABORT, 'Private source cannot be indexed publicly'); END;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER PrivateMediaInsertGuard; DROP TRIGGER PrivateMediaUpdateGuard;");
        migrationBuilder.DropTable("PrivateSources");
        migrationBuilder.DropTable("PrivateMediaRecords");
        migrationBuilder.DropTable("PrivateLibraryConfigurations");
        migrationBuilder.DropColumn("PrivacyPathHash", "MediaItems");
    }
}
