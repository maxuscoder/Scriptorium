using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Repositories;
using Scriptorium.Infrastructure.Caching;

namespace Scriptorium.Infrastructure.Repositories;

/// <summary>
/// Provides SQLite-backed access to tutorial collections and their lessons.
/// </summary>
public sealed class CourseRepository(
    IDbContextFactory<ScriptoriumDbContext> contextFactory,
    IMetadataCache? metadataCache = null)
    : Repository<Course>(contextFactory), ICourseRepository
{
    private readonly IMetadataCache _metadataCache = metadataCache ?? MetadataCache.ForOwner(contextFactory);

    /// <inheritdoc />
    public override async Task<Course?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.CourseById(id),
            [MetadataCacheKeys.CourseTag(id), MetadataCacheKeys.AllCoursesTag],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await Courses(context).SingleOrDefaultAsync(item => item.Id == id, token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(course);
        return course;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Course>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        return await Courses(context).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Course?> GetByMediaItemIdAsync(
        Guid mediaItemId,
        CancellationToken cancellationToken = default)
    {
        var course = await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.CourseByMediaItemId(mediaItemId),
            [MetadataCacheKeys.AllCoursesTag],
            async token =>
            {
                await using var context = await ContextFactory.CreateDbContextAsync(token);
                return await Courses(context)
                    .SingleOrDefaultAsync(item => item.Lessons.Any(lesson => lesson.MediaItemId == mediaItemId), token);
            },
            MetadataCacheCloner.Clone,
            cancellationToken);

        CacheAliases(course);
        return course;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CourseLibrarySummary>> GetLibrarySummariesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _metadataCache.GetOrCreateAsync(
            MetadataCacheKeys.CourseSummariesKey,
            [MetadataCacheKeys.AllCoursesTag],
            LoadLibrarySummariesAsync,
            summaries => summaries.ToArray(),
            cancellationToken) ?? [];
    }

    /// <inheritdoc />
    public async Task<bool> UpdateLessonOrderAsync(
        Guid courseId,
        IReadOnlyList<Guid> orderedLessonIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedLessonIds);

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var lessons = await context.Lessons
            .Where(lesson => lesson.CourseId == courseId)
            .ToListAsync(cancellationToken);
        if (lessons.Count != orderedLessonIds.Count ||
            orderedLessonIds.Count != orderedLessonIds.Distinct().Count())
        {
            return false;
        }

        var lessonsById = lessons.ToDictionary(lesson => lesson.Id);
        if (orderedLessonIds.Any(lessonId => !lessonsById.ContainsKey(lessonId)))
        {
            return false;
        }

        for (var index = 0; index < orderedLessonIds.Count; index++)
        {
            lessonsById[orderedLessonIds[index]].SortOrder = index;
        }

        var course = await context.Courses.SingleOrDefaultAsync(course => course.Id == courseId, cancellationToken);
        if (course is null)
        {
            return false;
        }

        course.IsOrderCustomized = true;
        await context.SaveChangesAsync(cancellationToken);
        InvalidateAllCourseMetadata();
        return true;
    }

    private static IQueryable<Course> Courses(ScriptoriumDbContext context) =>
        context.Courses
            .AsNoTracking()
            .Include(course => course.LibraryFolder)
            .Include(course => course.Lessons)
                .ThenInclude(lesson => lesson.MediaItem);

    private async Task<IReadOnlyList<CourseLibrarySummary>?> LoadLibrarySummariesAsync(CancellationToken cancellationToken)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken);
        var courses = await context.Courses
            .AsNoTracking()
            .Include(course => course.LibraryFolder)
            .OrderBy(course => course.Title)
            .ToListAsync(cancellationToken);
        var lessons = await context.Lessons
            .AsNoTracking()
            .Join(
                context.MediaItems.AsNoTracking(),
                lesson => lesson.MediaItemId,
                mediaItem => mediaItem.Id,
                (lesson, mediaItem) => new MediaSummaryRow(
                    lesson.CourseId,
                    mediaItem.ThumbnailPath,
                    mediaItem.TitleOverride,
                    mediaItem.DescriptionOverride,
                    mediaItem.ReleaseYearOverride,
                    mediaItem.ThumbnailOverride,
                    mediaItem.MediaTypeOverride,
                    mediaItem.TVShowTitleOverride,
                    mediaItem.SeasonNumberOverride,
                    mediaItem.EpisodeNumberOverride,
                    mediaItem.DateAdded,
                    mediaItem.LastPlayed,
                    mediaItem.RuntimeSeconds,
                    mediaItem.PlaybackPositionSeconds,
                    mediaItem.IsCompleted,
                    mediaItem.IsFavorite))
            .ToListAsync(cancellationToken);

        var lessonsByCourse = lessons.GroupBy(lesson => lesson.ParentId).ToDictionary(group => group.Key, group => group.ToArray());
        return courses.Select(course =>
        {
            var items = lessonsByCourse.GetValueOrDefault(course.Id) ?? [];
            var progress = items.Select(ProgressPercentage).DefaultIfEmpty(0).ToArray();
            return new CourseLibrarySummary(
                course.Id,
                course.Title,
                course.LibraryFolder.DisplayNameOrName,
                items.Select(item => item.ThumbnailPath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
                items.Length,
                items.Any(HasManualMetadata),
                items.Select(item => item.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Min(),
                items.Select(item => item.DateAdded).DefaultIfEmpty(DateTimeOffset.MinValue).Max(),
                items.Where(item => item.LastPlayed is not null).Select(item => item.LastPlayed).DefaultIfEmpty().Min(),
                items.Where(item => item.LastPlayed is not null).Select(item => item.LastPlayed).DefaultIfEmpty().Max(),
                progress.Min(),
                progress.Max(),
                items.Any(item => item.IsFavorite));
        }).ToArray();
    }

    private void CacheAliases(Course? course)
    {
        if (course is null)
        {
            return;
        }

        var tags = new[] { MetadataCacheKeys.CourseTag(course.Id), MetadataCacheKeys.AllCoursesTag };
        _metadataCache.Set(MetadataCacheKeys.CourseById(course.Id), tags, course, MetadataCacheCloner.Clone);
        foreach (var mediaItemId in course.Lessons.Select(lesson => lesson.MediaItemId))
        {
            _metadataCache.Set(
                MetadataCacheKeys.CourseByMediaItemId(mediaItemId),
                tags,
                course,
                MetadataCacheCloner.Clone);
        }
    }

    private void InvalidateAllCourseMetadata() =>
        _metadataCache.RemoveByTag(MetadataCacheKeys.AllCoursesTag);

    private static bool HasManualMetadata(MediaSummaryRow item) =>
        !string.IsNullOrWhiteSpace(item.TitleOverride) ||
        !string.IsNullOrWhiteSpace(item.DescriptionOverride) ||
        item.ReleaseYearOverride is not null ||
        item.ThumbnailOverride is not null ||
        item.MediaTypeOverride is not null ||
        !string.IsNullOrWhiteSpace(item.TVShowTitleOverride) ||
        item.SeasonNumberOverride is not null ||
        item.EpisodeNumberOverride is not null;

    private static double ProgressPercentage(MediaSummaryRow item) =>
        item.IsCompleted
            ? 100
            : item.RuntimeSeconds > 0
                ? Math.Clamp((double)item.PlaybackPositionSeconds / item.RuntimeSeconds.Value * 100, 0, 100)
                : 0;

    private sealed record MediaSummaryRow(
        Guid ParentId,
        string? ThumbnailPath,
        string? TitleOverride,
        string? DescriptionOverride,
        int? ReleaseYearOverride,
        string? ThumbnailOverride,
        MediaType? MediaTypeOverride,
        string? TVShowTitleOverride,
        int? SeasonNumberOverride,
        int? EpisodeNumberOverride,
        DateTimeOffset DateAdded,
        DateTimeOffset? LastPlayed,
        long? RuntimeSeconds,
        long PlaybackPositionSeconds,
        bool IsCompleted,
        bool IsFavorite);
}
