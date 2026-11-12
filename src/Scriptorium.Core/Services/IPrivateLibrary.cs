using Scriptorium.Core.Models;

namespace Scriptorium.Core.Services;

public enum PrivateProtectionMode { Standard, Encrypted }

/// <summary>Private metadata is only materialized by this authenticated boundary.</summary>
public sealed class PrivateMedia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VaultFileId { get; set; } = Guid.NewGuid();
    public required MediaItem Media { get; set; }
    public string Group { get; set; } = string.Empty;
    public bool OriginalRetained { get; set; }
}

public sealed record PrivateLibraryStatus(bool IsConfigured, bool IsUnlocked,
    PrivateProtectionMode Mode, int AutoLockMinutes);

public interface IPrivatePathGuard
{
    Task<bool> IsPrivatePathAsync(string path, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetExcludedPathHashesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> GetExcludedMediaIdsAsync(CancellationToken cancellationToken = default);
}

public interface IPrivateLibrary : IDisposable
{
    event EventHandler? Locked;
    event EventHandler? ContentsChanged;
    Task<PrivateLibraryStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task SetupAsync(PrivateProtectionMode mode, ReadOnlyMemory<byte> password, CancellationToken cancellationToken = default);
    Task<bool> UnlockAsync(ReadOnlyMemory<byte> password, CancellationToken cancellationToken = default);
    void Lock();
    Task ChangePasswordAsync(ReadOnlyMemory<byte> currentPassword, ReadOnlyMemory<byte> newPassword, CancellationToken cancellationToken = default);
    Task SetAutoLockAsync(int minutes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PrivateMedia>> BrowseAsync(CancellationToken cancellationToken = default);
    Task<PrivateMedia> ImportAsync(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task UpdateAsync(PrivateMedia item, CancellationToken cancellationToken = default);
    Task RemoveOriginalAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(Guid id, CancellationToken cancellationToken = default);
    Task ReturnToLibraryAsync(Guid id, string? destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}
