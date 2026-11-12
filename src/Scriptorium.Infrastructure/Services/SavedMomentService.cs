using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

public sealed class SavedMomentService(IDbContextFactory<ScriptoriumDbContext> contextFactory) : ISavedMomentService
{
    public async Task<IReadOnlyList<SavedMoment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SavedMoments.AsNoTracking().Include(moment => moment.MediaItem)
            .Include(moment => moment.Tags).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SavedMoment>> GetForMediaAsync(Guid mediaItemId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.SavedMoments.AsNoTracking().Where(moment => moment.MediaItemId == mediaItemId)
            .OrderBy(moment => moment.StartSeconds).ToListAsync(cancellationToken);
    }

    public async Task<bool> SaveAsync(SavedMoment moment, CancellationToken cancellationToken = default)
    {
        Validate(moment);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.MediaItems.AnyAsync(item => item.Id == moment.MediaItemId, cancellationToken)) return false;
        context.SavedMoments.Add(moment);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAsync(SavedMoment moment, CancellationToken cancellationToken = default)
    {
        Validate(moment);
        var names = moment.Tags.Select(tag => tag.Name.Trim()).Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SavedMoments.Include(m => m.Tags).SingleOrDefaultAsync(m => m.Id == moment.Id, cancellationToken);
        if (existing is null) return false;
        existing.Title = moment.Title.Trim();
        existing.Notes = string.IsNullOrWhiteSpace(moment.Notes) ? null : moment.Notes.Trim();
        existing.StartSeconds = moment.StartSeconds;
        existing.EndSeconds = moment.EndSeconds;
        foreach (var tag in existing.Tags.ToList())
            if (!names.Contains(tag.Name, StringComparer.OrdinalIgnoreCase)) context.SavedMomentTags.Remove(tag);
        foreach (var name in names)
            if (!existing.Tags.Any(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
                existing.Tags.Add(new SavedMomentTag { Name = name });
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.SavedMoments.FindAsync([id], cancellationToken);
        if (existing is null) return false;
        context.SavedMoments.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void Validate(SavedMoment moment)
    {
        ArgumentNullException.ThrowIfNull(moment);
        if (moment.StartSeconds < 0 || (moment.EndSeconds is { } end && end <= moment.StartSeconds))
            throw new ArgumentException("The end must be after a non-negative start.", nameof(moment));
        if (string.IsNullOrWhiteSpace(moment.Title))
            throw new ArgumentException("A title is required.", nameof(moment));
    }
}
