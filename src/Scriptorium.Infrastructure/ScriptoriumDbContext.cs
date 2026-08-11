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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Ignore<Movie>();
        modelBuilder.Ignore<TVShow>();
        modelBuilder.Ignore<Season>();
        modelBuilder.Ignore<Episode>();
        modelBuilder.Ignore<PlaybackProgress>();

        modelBuilder.Entity<LibraryFolder>(entity =>
        {
            entity.ToTable("LibraryFolders");
            entity.HasKey(folder => folder.Id);
            entity.Property(folder => folder.Path).IsRequired();
            entity.Property(folder => folder.Name).IsRequired();
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
