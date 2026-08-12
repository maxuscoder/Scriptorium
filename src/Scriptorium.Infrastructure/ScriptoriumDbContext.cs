using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Represents the application's local SQLite data store.
/// </summary>
public sealed class ScriptoriumDbContext(DbContextOptions<ScriptoriumDbContext> options) : DbContext(options)
{
    /// <summary>Gets the imported library folders.</summary>
    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();

    /// <summary>Gets the indexed media items.</summary>
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    /// <summary>Gets the user-defined categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Gets the television-show collections generated from scanned media.</summary>
    public DbSet<TVShow> TVShows => Set<TVShow>();

    /// <summary>Gets the seasons generated for television shows.</summary>
    public DbSet<Season> Seasons => Set<Season>();

    /// <summary>Gets the episodes assigned to generated seasons.</summary>
    public DbSet<Episode> Episodes => Set<Episode>();

    /// <summary>Gets the course collections generated from tutorial folders.</summary>
    public DbSet<Course> Courses => Set<Course>();

    /// <summary>Gets the lessons assigned to generated courses.</summary>
    public DbSet<Lesson> Lessons => Set<Lesson>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Ignore<Movie>();
        modelBuilder.Ignore<PlaybackProgress>();

        modelBuilder.Entity<LibraryFolder>(entity =>
        {
            entity.ToTable("LibraryFolders", table => table.HasCheckConstraint(
                "CK_LibraryFolders_MediaType",
                "\"MediaType\" IN (0, 1, 2)"));
            entity.HasKey(folder => folder.Id);
            entity.Property(folder => folder.Path).IsRequired();
            entity.Property(folder => folder.Name).IsRequired();
            entity.Property(folder => folder.DisplayName);
            entity.Property(folder => folder.MediaType)
                .HasDefaultValue(MediaType.Movie)
                .ValueGeneratedNever();
            entity.Ignore(folder => folder.DisplayNameOrName);
        });

        modelBuilder.Entity<TVShow>(entity =>
        {
            entity.ToTable("TVShows");
            entity.HasKey(show => show.Id);
            entity.Property(show => show.Title).IsRequired();
            entity.Property(show => show.EpisodeCount).HasDefaultValue(0);
            entity.HasIndex(show => new { show.LibraryFolderId, show.Title }).IsUnique();
            entity.HasOne(show => show.LibraryFolder)
                .WithMany(folder => folder.TVShows)
                .HasForeignKey(show => show.LibraryFolderId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.ToTable("Courses");
            entity.HasKey(course => course.Id);
            entity.Property(course => course.Title).IsRequired();
            entity.HasIndex(course => course.LibraryFolderId).IsUnique();
            entity.HasOne(course => course.LibraryFolder)
                .WithOne(folder => folder.Course)
                .HasForeignKey<Course>(course => course.LibraryFolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Lesson>(entity =>
        {
            entity.ToTable("Lessons");
            entity.HasKey(lesson => lesson.Id);
            entity.Property(lesson => lesson.Title).IsRequired();
            entity.Property(lesson => lesson.FilePath).IsRequired();
            entity.HasIndex(lesson => lesson.MediaItemId).IsUnique();
            entity.HasIndex(lesson => new { lesson.CourseId, lesson.SortOrder });
            entity.HasOne(lesson => lesson.Course)
                .WithMany(course => course.Lessons)
                .HasForeignKey(lesson => lesson.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(lesson => lesson.MediaItem)
                .WithOne()
                .HasForeignKey<Lesson>(lesson => lesson.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Season>(entity =>
        {
            entity.ToTable("Seasons", table => table.HasCheckConstraint(
                "CK_Seasons_SeasonNumber",
                "\"SeasonNumber\" > 0"));
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
            entity.HasIndex(episode => episode.MediaItemId).IsUnique();
            entity.HasIndex(episode => new { episode.SeasonId, episode.SortOrder });
            entity.Ignore(episode => episode.PlaybackProgress);
            entity.HasOne(episode => episode.Season)
                .WithMany(season => season.Episodes)
                .HasForeignKey(episode => episode.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(episode => episode.MediaItem)
                .WithOne()
                .HasForeignKey<Episode>(episode => episode.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name).IsRequired();
            entity.Property(category => category.Color).IsRequired();
        });

        modelBuilder.Entity<MediaItem>(entity =>
        {
            entity.ToTable("MediaItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).IsRequired();
            entity.Property(item => item.Path).IsRequired();
            entity.Property(item => item.PlaybackPositionSeconds).HasDefaultValue(0L);
            entity.Property(item => item.IsCompleted).HasDefaultValue(false);
            entity.Property(item => item.IsMissing).HasDefaultValue(false);
            entity.Property(item => item.TVShowTitle);
            entity.Property(item => item.SeasonNumber);
            entity.Property(item => item.EpisodeNumber);
            entity.HasIndex(item => item.Path);
            entity.HasIndex(item => item.LibraryFolderId);
            entity.HasIndex(item => item.CategoryId);

            entity.HasOne(item => item.LibraryFolder)
                .WithMany(folder => folder.MediaItems)
                .HasForeignKey(item => item.LibraryFolderId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(item => item.Category)
                .WithMany(category => category.MediaItems)
                .HasForeignKey(item => item.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
