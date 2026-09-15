using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure;
using Scriptorium.Infrastructure.Services;
using Xunit;

namespace Scriptorium.Tests.Infrastructure;

public sealed class MediaGroupingServiceTests
{
    [Fact]
    public async Task Updates_episode_season_and_persists_the_manual_override()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<ScriptoriumDbContext>()
            .UseSqlite(database)
            .Options;
        var folder = new LibraryFolder
        {
            Name = "TV",
            Path = @"C:\TV",
            MediaType = MediaType.TvShow
        };
        var mediaItem = new MediaItem
        {
            Title = "Episode",
            Path = @"C:\TV\episode.mkv",
            MediaType = MediaType.TvShow,
            DetectedMediaType = MediaType.TvShow,
            DetectedTVShowTitle = "Example",
            DetectedSeasonNumber = 1,
            DetectedEpisodeNumber = 3,
            TVShowTitle = "Example",
            SeasonNumber = 1,
            EpisodeNumber = 3,
            LibraryFolderId = folder.Id,
            LibraryFolder = folder
        };
        var show = new TVShow
        {
            Title = "Example",
            LibraryFolderId = folder.Id,
            LibraryFolder = folder,
            EpisodeCount = 1
        };
        var season = new Season
        {
            TVShow = show,
            TVShowId = show.Id,
            SeasonNumber = 1
        };
        season.Episodes.Add(new Episode
        {
            MediaItem = mediaItem,
            MediaItemId = mediaItem.Id,
            Season = season,
            SeasonId = season.Id,
            EpisodeNumber = 3,
            Title = mediaItem.Title,
            FilePath = mediaItem.Path
        });
        show.Seasons.Add(season);

        await using (var context = new ScriptoriumDbContext(options))
        {
            await context.Database.MigrateAsync();
            context.LibraryFolders.Add(folder);
            context.TVShows.Add(show);
            await context.SaveChangesAsync();
        }

        var service = new MediaGroupingService(new TestDbContextFactory(options));
        var changedMediaItemId = Guid.Empty;
        service.EpisodeSeasonChanged += mediaItemId => changedMediaItemId = mediaItemId;

        await service.UpdateEpisodeSeasonAsync(mediaItem.Id, 2);

        await using var verificationContext = new ScriptoriumDbContext(options);
        var storedMedia = await verificationContext.MediaItems.SingleAsync();
        Assert.Equal(2, storedMedia.SeasonNumber);
        Assert.Equal(2, storedMedia.SeasonNumberOverride);
        Assert.Equal(1, storedMedia.DetectedSeasonNumber);
        Assert.Equal(mediaItem.Id, changedMediaItemId);

        var storedShow = await verificationContext.TVShows
            .Include(value => value.Seasons)
                .ThenInclude(value => value.Episodes)
            .SingleAsync();
        var storedSeason = Assert.Single(storedShow.Seasons);
        Assert.Equal(2, storedSeason.SeasonNumber);
        Assert.Single(storedSeason.Episodes);
        Assert.Equal(mediaItem.Id, storedSeason.Episodes[0].MediaItemId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    public void Rejects_invalid_season_numbers(string? value)
    {
        Assert.Throws<ArgumentException>(() => MediaSeasonValidation.Normalize(value));
    }

    [Fact]
    public void Normalizes_positive_season_numbers()
    {
        Assert.Equal(12, MediaSeasonValidation.Normalize(" 12 "));
    }

    private sealed class TestDbContextFactory(DbContextOptions<ScriptoriumDbContext> options)
        : IDbContextFactory<ScriptoriumDbContext>
    {
        public ScriptoriumDbContext CreateDbContext() => new(options);

        public Task<ScriptoriumDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
