using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

public sealed class SavedMomentService(IDbContextFactory<ScriptoriumDbContext> contextFactory) : ISavedMomentService
{
    public async Task<bool> SaveAsync(SavedMoment moment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(moment);
        if (moment.StartSeconds < 0 || (moment.EndSeconds is { } end && end <= moment.StartSeconds))
            throw new ArgumentException("The end must be after a non-negative start.", nameof(moment));
        if (string.IsNullOrWhiteSpace(moment.Title))
            throw new ArgumentException("A title is required.", nameof(moment));
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.MediaItems.AnyAsync(item => item.Id == moment.MediaItemId, cancellationToken)) return false;
        context.SavedMoments.Add(moment);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
