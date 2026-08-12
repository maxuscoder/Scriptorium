using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class ScriptoriumDbContextTests
{
    [Fact]
    public async Task MigrateAsync_creates_the_current_flat_media_schema()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            await using var context = new ScriptoriumDbContext(CreateOptions(databasePath));
            await context.Database.MigrateAsync();

            var tableNames = await context.Database
                .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
                .ToListAsync();

            Assert.Subset(
                new HashSet<string>(tableNames),
                new HashSet<string> { "MediaItems", "Categories", "LibraryFolders" });
            Assert.DoesNotContain("Favorites", tableNames);
            Assert.Contains("TVShows", tableNames);
            Assert.Contains("Seasons", tableNames);
            Assert.Contains("Episodes", tableNames);
            Assert.Contains("Courses", tableNames);
            Assert.Contains("Lessons", tableNames);
            Assert.Equal(11, (await context.Database.GetAppliedMigrationsAsync()).Count());

            var folderColumns = await context.Database
                .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('LibraryFolders')")
                .ToListAsync();
            Assert.Contains("MediaType", folderColumns);

            var mediaItemColumns = await context.Database
                .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('MediaItems')")
                .ToListAsync();
            Assert.Contains("TVShowTitle", mediaItemColumns);
            Assert.Contains("SeasonNumber", mediaItemColumns);
            Assert.Contains("EpisodeNumber", mediaItemColumns);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void Model_configures_folder_and_category_foreign_keys()
    {
        using var context = new ScriptoriumDbContext(CreateOptions(":memory:"));

        AssertRelationship<MediaItem, LibraryFolder>(context, nameof(MediaItem.LibraryFolderId), false, DeleteBehavior.SetNull);
        AssertRelationship<MediaItem, Category>(context, nameof(MediaItem.CategoryId), false, DeleteBehavior.SetNull);
    }

    [Fact]
    public async Task Schema_upgrade_preserves_media_and_converts_favorite_and_runtime_data()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            await CreateLegacySchemaAsync(databasePath);

            await using var context = new ScriptoriumDbContext(CreateOptions(databasePath));
            await SqliteSchemaMigrator.UpgradeAsync(context);
            await LegacyDatabaseBaseliner.BaselineAsync(context, context.Database.GetMigrations().First());
            await context.Database.MigrateAsync();

            var item = await context.MediaItems.SingleAsync();
            Assert.True(item.IsFavorite);
            Assert.Equal(12, item.RuntimeSeconds);
            Assert.Equal(0, item.PlaybackPositionSeconds);
            Assert.False(item.IsCompleted);
            Assert.False(item.IsMissing);
            Assert.Null(item.MissingSince);
            Assert.NotEqual(Guid.Empty, item.LibraryFolderId);

            var tableNames = await context.Database
                .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
                .ToListAsync();
            Assert.DoesNotContain("Favorites", tableNames);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static DbContextOptions<ScriptoriumDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
            .Options;

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");

    private static async Task CreateLegacySchemaAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE "LibraryFolders" ("Id" TEXT NOT NULL PRIMARY KEY, "Path" TEXT NOT NULL, "Name" TEXT NOT NULL, "LastScanned" TEXT NULL, "IsEnabled" INTEGER NOT NULL);
            CREATE TABLE "Categories" ("Id" TEXT NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL, "Color" TEXT NOT NULL);
            CREATE TABLE "MediaItems" (
                "Id" TEXT NOT NULL PRIMARY KEY, "Title" TEXT NOT NULL, "Path" TEXT NOT NULL, "ThumbnailPath" TEXT NULL,
                "DateAdded" TEXT NOT NULL, "LastPlayed" TEXT NULL, "IsFavorite" INTEGER NOT NULL, "MediaType" INTEGER NOT NULL,
                "CategoryId" TEXT NULL, "Runtime" INTEGER NULL, "ReleaseYear" INTEGER NULL, "Description" TEXT NULL,
                "TVShow_Description" TEXT NULL, "TVShow_ReleaseYear" INTEGER NULL);
            CREATE TABLE "Favorites" ("MediaId" TEXT NOT NULL PRIMARY KEY, "DateAdded" TEXT NOT NULL);
            INSERT INTO "MediaItems" ("Id", "Title", "Path", "DateAdded", "IsFavorite", "MediaType", "Runtime")
            VALUES ('11111111-1111-1111-1111-111111111111', 'Example', 'C:\\Example.mp4', '2026-01-01T00:00:00+00:00', 0, 0, 120000000);
            INSERT INTO "Favorites" ("MediaId", "DateAdded")
            VALUES ('11111111-1111-1111-1111-111111111111', '2026-01-02T00:00:00+00:00');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertRelationship<TEntity, TPrincipal>(
        ScriptoriumDbContext context,
        string foreignKeyName,
        bool required,
        DeleteBehavior deleteBehavior)
        where TEntity : class
        where TPrincipal : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        Assert.Contains(
            entityType.GetForeignKeys(),
            foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(TPrincipal)
                          && foreignKey.Properties.Single().Name == foreignKeyName
                          && foreignKey.IsRequired == required
                          && foreignKey.DeleteBehavior == deleteBehavior);
    }
}
