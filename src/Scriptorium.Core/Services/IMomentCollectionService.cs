using Scriptorium.Core.Models;
namespace Scriptorium.Core.Services;

public interface IMomentCollectionService
{
    Task<IReadOnlyList<MomentCollection>> GetAllAsync();
    Task CreateAsync(string name);
    Task RenameAsync(Guid id, string name);
    Task DeleteAsync(Guid id);
    Task SetMembershipsAsync(Guid momentId, IReadOnlyCollection<Guid> collectionIds);
}
