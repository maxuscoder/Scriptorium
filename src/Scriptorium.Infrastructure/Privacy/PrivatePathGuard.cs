using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Privacy;

public sealed class PrivatePathGuard(IDbContextFactory<ScriptoriumDbContext> factory) : IPrivatePathGuard
{
    public async Task<bool> IsPrivatePathAsync(string path, CancellationToken cancellationToken = default)
    {
        var hash = PrivatePathHash.Compute(path);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.PrivateSources.AnyAsync(s => s.PathHash == hash, cancellationToken);
    }

    public async Task<IReadOnlySet<string>> GetExcludedPathHashesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return (await db.PrivateSources.Select(s => s.PathHash).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlySet<Guid>> GetExcludedMediaIdsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return (await db.PrivateSources.Select(s => s.PrivateMediaId).ToListAsync(cancellationToken)).ToHashSet();
    }
}
