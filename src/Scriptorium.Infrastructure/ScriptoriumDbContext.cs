using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Represents the application's normalized local SQLite data store.
/// </summary>
public sealed class ScriptoriumDbContext(DbContextOptions<ScriptoriumDbContext> options) : DbContext(options)
{
    /// <summary>Gets the library folders stored by the application.</summary>
    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();

    /// <summary>Gets the media items stored by the application.</summary>
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    /// <summary>Gets the tutorials stored by the application.</summary>
    public DbSet<Tutorial> Tutorials => Set<Tutorial>();

    /// <summary>Gets the movies stored by the application.</summary>
    public DbSet<Movie> Movies => Set<Movie>();

    /// <summary>Gets the television shows stored by the application.</summary>
    public DbSet<TVShow> TVShows => Set<TVShow>();

    /// <summary>Gets the seasons stored by the application.</summary>
    public DbSet<Season> Seasons => Set<Season>();

    /// <summary>Gets the episodes stored by the application.</summary>
    public DbSet<Episode> Episodes => Set<Episode>();

    /// <summary>Gets the user-defined categories stored by the application.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Gets category assignments for media items.</summary>
    public DbSet<MediaItemCategory> MediaItemCategories => Set<MediaItemCategory>();

    /// <summary>Gets the user's favorite media items.</summary>
    public DbSet<Favorite> Favorites => Set<Favorite>();

    /// <summary>Gets resumable playback states.</summary>
    public DbSet<PlaybackProgress> PlaybackProgresses => Set<PlaybackProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureLibraryFolders(modelBuilder);
        ConfigureMediaItems(modelBuilder);
        ConfigureCategories(modelBuilder);
        ConfigureFavorites(modelBuilder);
        ConfigureTelevision(modelBuilder);
        ConfigurePlaybackProgress(modelBuilder);
    }

    private static void ConfigureLibraryFolders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LibraryFolder>(entity =>
        {
            entity.ToTable("LibraryFolders");
            entity.HasKey(folder => folder.Id);
            entity.Property(folder => folder.Name).IsRequired();
            entity.Property(folder => folder.Path).IsRequired();
            entity.HasIndex(folder => folder.Path).IsUnique();

            entity.HasMany(folder => folder.MediaItems)
                .WithOne(item => item.LibraryFolder)
                .HasForeignKey(item => item.LibraryFolderId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureMediaItems(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MediaItem>(entity =>
        {
            entity.ToTable("MediaItems");
            entity.UseTptMappingStrategy();
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).IsRequired();
            entity.Property(item => item.Path).IsRequired();
            entity.HasIndex(item => item.Path).IsUnique();
        });

        modelBuilder.Entity<Movie>(entity =>
        {
            entity.ToTable("Movies");
            entity.Property(movie => movie.Runtime)
                .HasConversion(runtime => runtime.Ticks, ticks => TimeSpan.FromTicks(ticks));
        });

        modelBuilder.Entity<Tutorial>(entity => entity.ToTable("Tutorials"));

        modelBuilder.Entity<TVShow>(entity => entity.ToTable("TVShows"));
    }

    private static void ConfigureCategories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).IsRequired();
            entity.Property(category => category.Color).IsRequired();
            entity.HasIndex(category => category.Name).IsUnique();
        });

        modelBuilder.Entity<MediaItemCategory>(entity =>
        {
            entity.ToTable("MediaItemCategories");
            entity.HasKey(assignment => new { assignment.MediaItemId, assignment.CategoryId });

            entity.HasOne(assignment => assignment.MediaItem)
                .WithMany()
                .HasForeignKey(assignment => assignment.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(assignment => assignment.Category)
                .WithMany()
                .HasForeignKey(assignment => assignment.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaItem>()
            .HasMany(item => item.Categories)
            .WithMany(category => category.MediaItems)
            .UsingEntity<MediaItemCategory>();
    }

    private static void ConfigureFavorites(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Favorite>(entity =>
        {
            entity.ToTable("Favorites");
            entity.HasKey(favorite => favorite.MediaItemId);

            entity.HasOne(favorite => favorite.MediaItem)
                .WithOne(item => item.Favorite)
                .HasForeignKey<Favorite>(favorite => favorite.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureTelevision(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Season>(entity =>
        {
            entity.ToTable("Seasons");
            entity.HasKey(season => season.Id);
            entity.HasIndex(season => new { season.TVShowId, season.SeasonNumber }).IsUnique();

            entity.HasOne(season => season.TVShow)
                .WithMany(show => show.Seasons)
                .HasForeignKey(season => season.TVShowId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Episode>(entity =>
        {
            entity.ToTable("Episodes");
            entity.HasKey(episode => episode.Id);
            entity.Property(episode => episode.Title).IsRequired();
            entity.Property(episode => episode.FilePath).IsRequired();
            entity.Property(episode => episode.Duration)
                .HasConversion(duration => duration.Ticks, ticks => TimeSpan.FromTicks(ticks));
            entity.HasIndex(episode => new { episode.SeasonId, episode.EpisodeNumber }).IsUnique();
            entity.HasIndex(episode => episode.FilePath).IsUnique();

            entity.HasOne(episode => episode.Season)
                .WithMany(season => season.Episodes)
                .HasForeignKey(episode => episode.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePlaybackProgress(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlaybackProgress>(entity =>
        {
            entity.ToTable("PlaybackProgress", table => table.HasCheckConstraint(
                "CK_PlaybackProgresses_HasSingleOwner",
                "(MediaItemId IS NOT NULL AND EpisodeId IS NULL) OR (MediaItemId IS NULL AND EpisodeId IS NOT NULL)"));
            entity.HasKey(progress => progress.Id);
            entity.Property(progress => progress.CurrentPosition)
                .HasConversion(position => position.Ticks, ticks => TimeSpan.FromTicks(ticks));
            entity.Property(progress => progress.Duration)
                .HasConversion(duration => duration.Ticks, ticks => TimeSpan.FromTicks(ticks));
            entity.HasIndex(progress => progress.MediaItemId).IsUnique();
            entity.HasIndex(progress => progress.EpisodeId).IsUnique();

            entity.HasOne(progress => progress.MediaItem)
                .WithOne(item => item.PlaybackProgress)
                .HasForeignKey<PlaybackProgress>(progress => progress.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(progress => progress.Episode)
                .WithOne(episode => episode.PlaybackProgress)
                .HasForeignKey<PlaybackProgress>(progress => progress.EpisodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
