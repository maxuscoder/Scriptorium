using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class ScriptoriumDbContextTests
{
    [Fact]
    public async Task EnsureCreatedAsync_creates_the_complete_normalized_schema()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scriptorium-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True;Pooling=False")
                .Options;

            await using (var context = new ScriptoriumDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();

                var tableNames = await context.Database
                    .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
                    .ToListAsync();

                Assert.Subset(
                    new HashSet<string>(tableNames),
                    new HashSet<string>
                    {
                        "MediaItems",
                        "Tutorials",
                        "TVShows",
                        "Seasons",
                        "Episodes",
                        "Movies",
                        "Categories",
                        "LibraryFolders",
                        "PlaybackProgress",
                        "Favorites"
                    });
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public void Model_configures_the_expected_foreign_key_relationships()
    {
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new ScriptoriumDbContext(options);

        AssertRelationship<MediaItem, LibraryFolder>(context, nameof(MediaItem.LibraryFolderId), DeleteBehavior.SetNull);
        AssertRelationship<Season, TVShow>(context, nameof(Season.TVShowId), DeleteBehavior.Cascade);
        AssertRelationship<Episode, Season>(context, nameof(Episode.SeasonId), DeleteBehavior.Cascade);
        AssertRelationship<Favorite, MediaItem>(context, nameof(Favorite.MediaItemId), DeleteBehavior.Cascade);
        AssertRelationship<MediaItemCategory, MediaItem>(context, nameof(MediaItemCategory.MediaItemId), DeleteBehavior.Cascade);
        AssertRelationship<MediaItemCategory, Category>(context, nameof(MediaItemCategory.CategoryId), DeleteBehavior.Cascade);
        AssertRelationship<PlaybackProgress, MediaItem>(context, nameof(PlaybackProgress.MediaItemId), DeleteBehavior.Cascade);
        AssertRelationship<PlaybackProgress, Episode>(context, nameof(PlaybackProgress.EpisodeId), DeleteBehavior.Cascade);
    }

    private static void AssertRelationship<TEntity, TPrincipal>(
        ScriptoriumDbContext context,
        string foreignKeyName,
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
                          && foreignKey.DeleteBehavior == deleteBehavior);
    }
}
