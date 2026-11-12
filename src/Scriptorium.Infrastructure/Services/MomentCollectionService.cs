using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;

namespace Scriptorium.Infrastructure.Services;

public sealed class MomentCollectionService(IDbContextFactory<ScriptoriumDbContext> factory) : IMomentCollectionService
{
    public async Task<IReadOnlyList<MomentCollection>> GetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.MomentCollections.AsNoTracking().Include(c => c.Memberships).OrderBy(c => c.Name).ToListAsync();
    }
    public async Task CreateAsync(string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.MomentCollections.Add(new MomentCollection { Name = ValidateName(name) });
        await db.SaveChangesAsync();
    }
    public async Task RenameAsync(Guid id, string name)
    {
        await using var db = await factory.CreateDbContextAsync();
        var collection = await db.MomentCollections.FindAsync(id) ?? throw new InvalidOperationException("Collection no longer exists.");
        collection.Name = ValidateName(name);
        await db.SaveChangesAsync();
    }
    public async Task DeleteAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var collection = await db.MomentCollections.FindAsync(id);
        if (collection is null) return;
        db.MomentCollections.Remove(collection);
        await db.SaveChangesAsync();
    }
    public async Task SetMembershipsAsync(Guid momentId, IReadOnlyCollection<Guid> collectionIds)
    {
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.SavedMoments.AnyAsync(m => m.Id == momentId)) throw new InvalidOperationException("Moment no longer exists.");
        var ids = collectionIds.Distinct().ToArray();
        if (await db.MomentCollections.CountAsync(c => ids.Contains(c.Id)) != ids.Length) throw new InvalidOperationException("A collection no longer exists.");
        var existing = await db.MomentCollectionMemberships.Where(m => m.SavedMomentId == momentId).ToListAsync();
        db.MomentCollectionMemberships.RemoveRange(existing.Where(m => !ids.Contains(m.CollectionId)));
        foreach (var id in ids.Where(id => existing.All(m => m.CollectionId != id)))
            db.MomentCollectionMemberships.Add(new MomentCollectionMembership { CollectionId = id, SavedMomentId = momentId });
        await db.SaveChangesAsync();
    }
    private static string ValidateName(string name) => !string.IsNullOrWhiteSpace(name) ? name.Trim() : throw new ArgumentException("Enter a collection name.");
}
