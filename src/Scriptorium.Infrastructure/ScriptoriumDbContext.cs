using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Infrastructure.Privacy;

namespace Scriptorium.Infrastructure;

/// <summary>
/// Represents the application's local SQLite data store.
/// </summary>
public sealed class ScriptoriumDbContext(DbContextOptions<ScriptoriumDbContext> options) : DbContext(options)
{
    public DbSet<PrivateLibraryConfiguration> PrivateLibraryConfigurations => Set<PrivateLibraryConfiguration>();
    public DbSet<PrivateMediaRecord> PrivateMediaRecords => Set<PrivateMediaRecord>();
    public DbSet<PrivateSource> PrivateSources => Set<PrivateSource>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PreparePrivacyPaths();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PreparePrivacyPaths();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PreparePrivacyPaths()
    {
        foreach (var entry in ChangeTracker.Entries<MediaItem>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            entry.Entity.PrivacyPathHash = PrivatePathHash.Compute(entry.Entity.Path);
    }
    /// <summary>Gets the imported library folders.</summary>
    public DbSet<LibraryFolder> LibraryFolders => Set<LibraryFolder>();

    /// <summary>Gets the indexed media items.</summary>
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    /// <summary>Gets the saved timestamps and sequences.</summary>
    public DbSet<MomentCollection> MomentCollections => Set<MomentCollection>();
    public DbSet<MomentCollectionMembership> MomentCollectionMemberships => Set<MomentCollectionMembership>();
    public DbSet<SavedMoment> SavedMoments => Set<SavedMoment>();

    /// <summary>Gets the tags assigned to saved moments.</summary>
    public DbSet<SavedMomentTag> SavedMomentTags => Set<SavedMomentTag>();

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

        modelBuilder.Entity<PrivateLibraryConfiguration>().HasKey(c => c.Id);
        modelBuilder.Entity<PrivateMediaRecord>().HasKey(c => c.Id);
        modelBuilder.Entity<PrivateSource>().HasKey(c => c.PathHash);
        modelBuilder.Entity<SavedMoment>().HasQueryFilter(m => MediaItems.Any(i => i.Id == m.MediaItemId));
        modelBuilder.Entity<SavedMomentTag>().HasQueryFilter(t => SavedMoments.Any(m => m.Id == t.SavedMomentId));
        modelBuilder.Entity<MomentCollectionMembership>().HasQueryFilter(t => SavedMoments.Any(m => m.Id == t.SavedMomentId));
        modelBuilder.Entity<Episode>().HasQueryFilter(e => MediaItems.Any(i => i.Id == e.MediaItemId));
        modelBuilder.Entity<Lesson>().HasQueryFilter(e => MediaItems.Any(i => i.Id == e.MediaItemId));

        modelBuilder.Entity<LibraryFolder>(entity =>
        {
            entity.ToTable("LibraryFolders", table => table.HasCheckConstraint(
                "CK_LibraryFolders_MediaType",
                "\"MediaType\" IN (0, 1, 2)"));
            entity.HasKey(folder => folder.Id);
            entity.Property(folder => folder.Path).IsRequired();
            entity.Property(folder => folder.Path).UseCollation("NOCASE");
            entity.HasIndex(folder => folder.Path).IsUnique();
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
            entity.Property(show => show.Title)
                .IsRequired()
                .UseCollation("NOCASE");
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
            entity.Property(course => course.IsOrderCustomized).HasDefaultValue(false);
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

        modelBuilder.Entity<SavedMoment>(entity =>
        {
            entity.ToTable("SavedMoments", table => table.HasCheckConstraint(
                "CK_SavedMoments_TimeRange",
                "\"StartSeconds\" >= 0 AND (\"EndSeconds\" IS NULL OR \"EndSeconds\" > \"StartSeconds\")"));
            entity.HasKey(moment => moment.Id);
            entity.Property(moment => moment.Title).IsRequired();
            entity.HasIndex(moment => new { moment.MediaItemId, moment.StartSeconds });
            entity.HasOne(moment => moment.MediaItem)
                .WithMany(item => item.SavedMoments)
                .HasForeignKey(moment => moment.MediaItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MomentCollection>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired();
        });
        modelBuilder.Entity<MomentCollectionMembership>(entity =>
        {
            entity.HasKey(m => new { m.CollectionId, m.SavedMomentId });
            entity.HasOne(m => m.Collection).WithMany(c => c.Memberships).HasForeignKey(m => m.CollectionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.SavedMoment).WithMany(m => m.CollectionMemberships).HasForeignKey(m => m.SavedMomentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavedMomentTag>(entity =>
        {
            entity.ToTable("SavedMomentTags");
            entity.HasKey(tag => new { tag.SavedMomentId, tag.Name });
            entity.Property(tag => tag.Name).IsRequired();
            entity.HasOne(tag => tag.SavedMoment)
                .WithMany(moment => moment.Tags)
                .HasForeignKey(tag => tag.SavedMomentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(category => category.Id);
            entity.Property(category => category.Name)
                .UseCollation("NOCASE")
                .IsRequired();
            entity.Property(category => category.Color).IsRequired();
            entity.HasIndex(category => category.Name).IsUnique();
        });

        modelBuilder.Entity<MediaItem>(entity =>
        {
            // Always applied, including while the Private Library is unlocked.
            entity.HasQueryFilter(item => !PrivateSources.Any(source => source.PathHash == item.PrivacyPathHash));
            entity.Property(item => item.PrivacyPathHash).HasDefaultValue(string.Empty);
            entity.ToTable("MediaItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).IsRequired();
            entity.Property(item => item.TitleOverride);
            entity.Property(item => item.DescriptionOverride);
            entity.Property(item => item.ReleaseYearOverride);
            entity.Property(item => item.DetectedThumbnailPath);
            entity.Property(item => item.ThumbnailOverride);
            entity.Property(item => item.DetectedMediaType);
            entity.Property(item => item.MediaTypeOverride);
            entity.Property(item => item.Path)
                .UseCollation("NOCASE")
                .IsRequired();
            entity.Property(item => item.PlaybackPositionSeconds).HasDefaultValue(0L);
            entity.Property(item => item.IsCompleted).HasDefaultValue(false);
            entity.HasIndex(item => item.LastPlayedUnixTimeMilliseconds);
            entity.Property(item => item.IsMissing).HasDefaultValue(false);
            entity.Property(item => item.TVShowTitle);
            entity.Property(item => item.SeasonNumber);
            entity.Property(item => item.EpisodeNumber);
            entity.HasIndex(item => item.Path).IsUnique();
            entity.HasIndex(item => item.LibraryFolderId);
            entity.HasIndex(item => item.CategoryId);
            entity.HasIndex(item => new { item.MediaType, item.CategoryId });
            entity.HasIndex(item => new { item.MediaType, item.DateAdded });
            entity.HasIndex(item => new { item.MediaType, item.LastPlayedUnixTimeMilliseconds });

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
