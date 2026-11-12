using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scriptorium.Core.Models;
using Scriptorium.Core.Services;
using Scriptorium.Infrastructure.Caching;

namespace Scriptorium.Infrastructure.Privacy;

/// <summary>Owns authentication, encrypted metadata, recoverable transfers, and revocable playback leases.</summary>
public sealed class PrivateLibraryService(
    IDbContextFactory<ScriptoriumDbContext> factory, PrivateVaultOptions options,
    IMetadataCache? cache = null, IMediaScannerService? scanner = null,
    ITvShowHierarchySynchronizer? tvShows = null, ITutorialCourseSynchronizer? tutorials = null) : IPrivateLibrary
{
    private readonly IMetadataCache _cache = cache ?? MetadataCache.ForOwner(factory);
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _sync = new();
    private readonly List<WeakReference<Stream>> _streams = [];
    private byte[]? _masterKey;
    private CancellationTokenSource _session = new();
    private int _epoch;
    private bool _disposed;
    private DateTimeOffset _nextUnlock;
    private int _failedAttempts;
    private static readonly JsonSerializerOptions JsonOptions = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles };
    public event EventHandler? Locked;
    public event EventHandler? ContentsChanged;

    public async Task<PrivateLibraryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var config = await db.PrivateLibraryConfigurations.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        lock (_sync) return new(config is not null, _masterKey is not null, (PrivateProtectionMode)(config?.Mode ?? 0), config?.AutoLockMinutes ?? 15);
    }

    private static byte[] KeyAad(int mode) => Encoding.UTF8.GetBytes($"Scriptorium/private/key/v1/{mode}");
    private static void ValidateConfiguration(PrivateLibraryConfiguration configuration)
    {
        if (configuration.Mode is < 0 or > 1 || configuration.Salt.Length != 16 || configuration.WrappedKey.Length != 60)
            throw new CryptographicException("The Private Library key configuration is damaged.");
    }
    private static void ValidatePassword(ReadOnlyMemory<byte> password)
    {
        if (password.Length > 1024 || Encoding.UTF8.GetCharCount(password.Span) < 12)
            throw new ArgumentException("Use a password of at least 12 characters (up to 1024 UTF-8 bytes).");
    }

    public async Task SetupAsync(PrivateProtectionMode mode, ReadOnlyMemory<byte> password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        await _operations.WaitAsync(cancellationToken);
        byte[]? key = null;
        byte[]? wrappingKey = null;
        var epoch = _epoch;
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            if (await db.PrivateLibraryConfigurations.AnyAsync(cancellationToken)) throw new InvalidOperationException("The Private Library is already configured.");
            var salt = RandomNumberGenerator.GetBytes(16);
            key = RandomNumberGenerator.GetBytes(32);
            wrappingKey = await VaultCryptography.DeriveAsync(password, salt);
            cancellationToken.ThrowIfCancellationRequested();
            db.PrivateLibraryConfigurations.Add(new() { Mode = (int)mode, Salt = salt,
                WrappedKey = VaultCryptography.Seal(wrappingKey, key, KeyAad((int)mode)) });
            await db.SaveChangesAsync(cancellationToken);
            InstallKey(key, epoch);
            key = null;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (wrappingKey is not null) CryptographicOperations.ZeroMemory(wrappingKey);
            _operations.Release();
        }
    }

    public async Task<bool> UnlockAsync(ReadOnlyMemory<byte> password, CancellationToken cancellationToken = default)
    {
        if (password.Length > 1024) return false;
        await _operations.WaitAsync(cancellationToken);
        byte[]? wrappingKey = null;
        byte[]? key = null;
        var epoch = _epoch;
        try
        {
            if (DateTimeOffset.UtcNow < _nextUnlock) throw new InvalidOperationException("Please wait a few seconds before trying again.");
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            var config = await db.PrivateLibraryConfigurations.SingleAsync(cancellationToken);
            ValidateConfiguration(config);
            wrappingKey = await VaultCryptography.DeriveAsync(password, config.Salt);
            cancellationToken.ThrowIfCancellationRequested();
            try { key = VaultCryptography.Unseal(wrappingKey, config.WrappedKey, KeyAad(config.Mode)); }
            catch (CryptographicException)
            {
                _failedAttempts++;
                _nextUnlock = DateTimeOffset.UtcNow.AddSeconds(Math.Min(30, Math.Pow(2, Math.Min(_failedAttempts - 1, 5))));
                return false;
            }
            InstallKey(key, epoch);
            key = null;
            _failedAttempts = 0;
            return true;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (wrappingKey is not null) CryptographicOperations.ZeroMemory(wrappingKey);
            _operations.Release();
        }
    }

    private void InstallKey(byte[] key, int epoch)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_epoch != epoch) throw new OperationCanceledException("The library was locked while authenticating.");
            if (_masterKey is not null) CryptographicOperations.ZeroMemory(_masterKey);
            _masterKey = key;
            if (_session.IsCancellationRequested) { _session.Dispose(); _session = new(); }
        }
    }

    public void Lock()
    {
        lock (_sync)
        {
            _epoch++;
            _session.Cancel();
            if (_masterKey is not null) CryptographicOperations.ZeroMemory(_masterKey);
            _masterKey = null;
            foreach (var weak in _streams) if (weak.TryGetTarget(out var stream)) stream.Dispose();
            _streams.Clear();
        }
        Locked?.Invoke(this, EventArgs.Empty);
    }

    private async Task<T> AuthenticatedAsync<T>(Func<byte[], CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _operations.WaitAsync(cancellationToken);
        byte[]? key = null;
        try
        {
            CancellationToken session;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                key = _masterKey?.ToArray() ?? throw new UnauthorizedAccessException("Unlock the Private Library first.");
                session = _session.Token;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(session, cancellationToken);
            var result = await action(key, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            _operations.Release();
        }
    }

    public Task ChangePasswordAsync(ReadOnlyMemory<byte> currentPassword, ReadOnlyMemory<byte> newPassword, CancellationToken cancellationToken = default)
    {
        ValidatePassword(newPassword);
        if (currentPassword.Length > 1024) throw new ArgumentException("The current password is too long.");
        return AuthenticatedAsync(async (key, token) =>
        {
            await using var db = await factory.CreateDbContextAsync(token);
            var config = await db.PrivateLibraryConfigurations.SingleAsync(token);
            ValidateConfiguration(config);
            var oldWrapping = await VaultCryptography.DeriveAsync(currentPassword, config.Salt);
            byte[]? verified = null;
            byte[]? newWrapping = null;
            try
            {
                verified = VaultCryptography.Unseal(oldWrapping, config.WrappedKey, KeyAad(config.Mode));
                if (!CryptographicOperations.FixedTimeEquals(verified, key)) throw new CryptographicException();
                config.Salt = RandomNumberGenerator.GetBytes(16);
                newWrapping = await VaultCryptography.DeriveAsync(newPassword, config.Salt);
                config.WrappedKey = VaultCryptography.Seal(newWrapping, key, KeyAad(config.Mode));
                await db.SaveChangesAsync(token);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(oldWrapping);
                if (verified is not null) CryptographicOperations.ZeroMemory(verified);
                if (newWrapping is not null) CryptographicOperations.ZeroMemory(newWrapping);
            }
        }, cancellationToken);
    }

    public Task SetAutoLockAsync(int minutes, CancellationToken cancellationToken = default) => AuthenticatedAsync(async (_, token) =>
    {
        if (minutes is < 0 or > 240) throw new ArgumentOutOfRangeException(nameof(minutes));
        await using var db = await factory.CreateDbContextAsync(token);
        var config = await db.PrivateLibraryConfigurations.SingleAsync(token);
        config.AutoLockMinutes = minutes;
        await db.SaveChangesAsync(token);
        return true;
    }, cancellationToken);

    private static byte[] Serialize(byte[] key, PrivateMedia item)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions);
        try { return VaultCryptography.Seal(key, plaintext, item.Id.ToByteArray()); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static PrivateMedia Deserialize(byte[] key, PrivateMediaRecord record)
    {
        var plaintext = VaultCryptography.Unseal(key, record.Payload, record.Id.ToByteArray());
        try
        {
            var item = JsonSerializer.Deserialize<PrivateMedia>(plaintext, JsonOptions) ?? throw new InvalidDataException("Private metadata is damaged.");
            if (item.Id != record.Id) throw new InvalidDataException("Private metadata identity is damaged.");
            return item;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public Task<IReadOnlyList<PrivateMedia>> BrowseAsync(CancellationToken cancellationToken = default) => AuthenticatedAsync<IReadOnlyList<PrivateMedia>>(async (key, token) =>
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var records = await db.PrivateMediaRecords.AsNoTracking().ToListAsync(token);
        var items = records.Select(r => Deserialize(key, r)).ToList();
        token.ThrowIfCancellationRequested();
        return items;
    }, cancellationToken);

    public Task<PrivateMedia> ImportAsync(string path, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => AuthenticatedAsync(async (key, token) =>
    {
        // A scan can hold stale metadata. Do not begin a transfer while it is running.
        if (scanner?.IsScanning == true) throw new InvalidOperationException("Wait for the library scan to finish before importing private media.");
        path = Path.GetFullPath(path);
        var sourceHash = PrivatePathHash.Compute(path);
        await using var db = await factory.CreateDbContextAsync(token);
        if (await db.PrivateSources.AnyAsync(s => s.PathHash == sourceHash, token)) throw new InvalidOperationException("This file is already private.");
        var config = await db.PrivateLibraryConfigurations.SingleAsync(token);
        // FileShare.Read prevents source modification or deletion during encryption on Windows.
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var media = await db.MediaItems.Include(m => m.SavedMoments).ThenInclude(m => m.Tags).SingleOrDefaultAsync(m => m.Path == path, token)
            ?? new MediaItem { Title = Path.GetFileNameWithoutExtension(path), Path = path, FileSize = source.Length,
                MediaType = MediaFileClassifier.Suggest(path, Path.GetDirectoryName(path)!).Type };
        var item = new PrivateMedia { Id = media.Id, Media = media, Group = media.TVShowTitle ?? Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty,
            OriginalRetained = config.Mode == (int)PrivateProtectionMode.Encrypted };
        var finalPath = VaultPath(item.VaultFileId);
        var staging = finalPath + "." + Guid.NewGuid().ToString("N") + ".partial";
        var committed = false;
        var ownsFinalPath = false;
        try
        {
            if (config.Mode == (int)PrivateProtectionMode.Encrypted)
            {
                Directory.CreateDirectory(options.DirectoryPath);
                await using (var destination = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await ChunkedVaultFile.EncryptAsync(source, destination, key, progress, token, fileId: item.VaultFileId);
                    await destination.FlushAsync(token);
                    destination.Flush(flushToDisk: true);
                }
                // Verify the complete ciphertext before committing metadata. Never delete the original.
                using (var verify = new VaultReadStream(staging, key, token, item.VaultFileId))
                {
                    var verifyBuffer = new byte[65536];
                    try { while (await verify.ReadAsync(verifyBuffer, token) != 0) { } }
                    finally { CryptographicOperations.ZeroMemory(verifyBuffer); }
                }
                File.Move(staging, finalPath);
                ownsFinalPath = true;
            }
            media.LibraryFolder = null;
            media.Category = null;
            foreach (var moment in media.SavedMoments)
            {
                moment.MediaItem = null!;
                moment.CollectionMemberships.Clear();
                foreach (var tag in moment.Tags) tag.SavedMoment = null!;
            }
            // Private browsing never populates the public image or disk thumbnail caches.
            media.ThumbnailPath = media.DetectedThumbnailPath = media.ThumbnailOverride = null;
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            // Detach the snapshot; delete public metadata and dependent moments/episodes atomically.
            db.ChangeTracker.Clear();
            await db.MediaItems.Where(m => m.Id == media.Id).ExecuteDeleteAsync(token);
            db.PrivateMediaRecords.Add(new() { Id = item.Id, Payload = Serialize(key, item) });
            db.PrivateSources.Add(new() { PathHash = sourceHash, PrivateMediaId = item.Id });
            await db.SaveChangesAsync(token);
            await RemoveEmptyHierarchyAsync(db, token);
            await transaction.CommitAsync(token);
            committed = true;
            InvalidatePublicCaches();
            ContentsChanged?.Invoke(this, EventArgs.Empty);
            return item;
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
            if (!committed && ownsFinalPath && File.Exists(finalPath)) File.Delete(finalPath);
        }
    }, cancellationToken);

    public Task UpdateAsync(PrivateMedia item, CancellationToken cancellationToken = default) => AuthenticatedAsync(async (key, token) =>
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var record = await db.PrivateMediaRecords.SingleAsync(r => r.Id == item.Id, token);
        var existing = Deserialize(key, record);
        // Callers may edit presentation and progress, but cannot redirect playback to another source.
        item.Media.Path = existing.Media.Path;
        item.Media.Id = existing.Media.Id;
        item.VaultFileId = existing.VaultFileId;
        item.OriginalRetained = existing.OriginalRetained;
        record.Payload = Serialize(key, item);
        await db.SaveChangesAsync(token);
        return true;
    }, cancellationToken);

    public Task<Stream> OpenReadAsync(Guid id, CancellationToken cancellationToken = default) => AuthenticatedAsync<Stream>(async (key, token) =>
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var item = Deserialize(key, await db.PrivateMediaRecords.SingleAsync(r => r.Id == id, token));
        var config = await db.PrivateLibraryConfigurations.SingleAsync(token);
        lock (_sync)
        {
            token.ThrowIfCancellationRequested();
            Stream stream = config.Mode == (int)PrivateProtectionMode.Encrypted
                ? new VaultReadStream(VaultPath(item.VaultFileId), key, _session.Token, item.VaultFileId)
                : new FileStream(item.Media.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
            _streams.RemoveAll(w => !w.TryGetTarget(out _));
            _streams.Add(new(stream));
            return stream;
        }
    }, cancellationToken);

    public Task RemoveOriginalAsync(Guid id, CancellationToken cancellationToken = default) => AuthenticatedAsync(async (key, token) =>
    {
        await using var db = await factory.CreateDbContextAsync(token);
        var config = await db.PrivateLibraryConfigurations.SingleAsync(token);
        if (config.Mode != (int)PrivateProtectionMode.Encrypted) throw new InvalidOperationException("Standard Protection needs the original file for playback.");
        var record = await db.PrivateMediaRecords.SingleAsync(r => r.Id == id, token);
        var item = Deserialize(key, record);
        using var original = OriginalFileRemoval.Open(item.Media.Path);
        using var encrypted = new VaultReadStream(VaultPath(item.VaultFileId), key, token, item.VaultFileId);
        if (original.Length != encrypted.Length) throw new IOException("The original changed after import. It was kept.");
        var originalHash = await SHA256.HashDataAsync(original, token);
        var vaultHash = await SHA256.HashDataAsync(encrypted, token);
        if (!CryptographicOperations.FixedTimeEquals(originalHash, vaultHash))
            throw new IOException("The original changed after import or the vault is damaged. It was kept.");
        token.ThrowIfCancellationRequested();
        OriginalFileRemoval.DeleteOnClose(original);
        item.OriginalRetained = false;
        record.Payload = Serialize(key, item);
        await db.SaveChangesAsync(token);
        return true;
    }, cancellationToken);

    public Task ReturnToLibraryAsync(Guid id, string? destination, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => AuthenticatedAsync(async (key, token) =>
    {
        if (scanner?.IsScanning == true) throw new InvalidOperationException("Wait for the library scan to finish before returning private media.");
        await using var db = await factory.CreateDbContextAsync(token);
        var record = await db.PrivateMediaRecords.SingleAsync(r => r.Id == id, token);
        var item = Deserialize(key, record);
        var config = await db.PrivateLibraryConfigurations.SingleAsync(token);
        var path = item.Media.Path;
        string? partial = null;
        if (config.Mode == (int)PrivateProtectionMode.Encrypted)
        {
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Choose a destination for the decrypted file.");
            path = Path.GetFullPath(destination);
            if (File.Exists(path)) throw new IOException("Choose a new file name. Existing files will not be overwritten.");
            partial = path + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using var source = new VaultReadStream(VaultPath(item.VaultFileId), key, token, item.VaultFileId);
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                {
                    var buffer = new byte[65536];
                    try
                    {
                        int read;
                        long completed = 0;
                        while ((read = await source.ReadAsync(buffer, token)) != 0)
                        {
                            await output.WriteAsync(buffer.AsMemory(0, read), token);
                            completed += read;
                            progress?.Report((double)completed / source.Length);
                        }
                        await output.FlushAsync(token);
                        output.Flush(true);
                    }
                    finally { CryptographicOperations.ZeroMemory(buffer); }
                }
                File.Move(partial, path);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        // A failed database commit retains the private record and ciphertext for retry.
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.PrivateSources.Where(s => s.PrivateMediaId == id).ExecuteDeleteAsync(token);
        db.PrivateMediaRecords.Remove(record);
        item.Media.Path = path;
        if (item.Media.LibraryFolderId is { } folderId && !await db.LibraryFolders.AnyAsync(f => f.Id == folderId, token)) item.Media.LibraryFolderId = null;
        if (item.Media.CategoryId is { } categoryId && !await db.Categories.AnyAsync(c => c.Id == categoryId, token)) item.Media.CategoryId = null;
        item.Media.LibraryFolder = null;
        item.Media.Category = null;
        foreach (var moment in item.Media.SavedMoments)
        {
            moment.MediaItem = item.Media;
            foreach (var tag in moment.Tags) tag.SavedMoment = moment;
        }
        db.MediaItems.Add(item.Media);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        // Reuse the normal organizers so returned episodes and lessons immediately reappear in collections.
        if (tvShows is not null) await tvShows.SynchronizeAsync([item.Media], CancellationToken.None);
        if (tutorials is not null && item.Media.LibraryFolderId is { } restoredFolder)
        {
            var folders = await db.LibraryFolders.AsNoTracking().Where(f => f.Id == restoredFolder).ToListAsync();
            await tutorials.SynchronizeAsync(folders, [item.Media], CancellationToken.None);
        }
        // Keep open playback handles safe: locking/closing the player precedes exports in the UI.
        if (config.Mode == (int)PrivateProtectionMode.Encrypted && File.Exists(VaultPath(item.VaultFileId)))
        {
            try { File.Delete(VaultPath(item.VaultFileId)); } catch (IOException) { /* Safe encrypted orphan, never plaintext. */ }
        }
        InvalidatePublicCaches();
        ContentsChanged?.Invoke(this, EventArgs.Empty);
        progress?.Report(1);
        return true;
    }, cancellationToken);

    private string VaultPath(Guid id) => Path.Combine(options.DirectoryPath, id.ToString("N") + ".spv");

    private static async Task RemoveEmptyHierarchyAsync(ScriptoriumDbContext db, CancellationToken token)
    {
        await db.Seasons.Where(s => !s.Episodes.Any()).ExecuteDeleteAsync(token);
        await db.TVShows.Where(s => !s.Seasons.Any()).ExecuteDeleteAsync(token);
        await db.TVShows.ExecuteUpdateAsync(setters => setters.SetProperty(s => s.EpisodeCount,
            s => s.Seasons.SelectMany(season => season.Episodes).Count()), token);
        await db.Courses.Where(c => !c.Lessons.Any()).ExecuteDeleteAsync(token);
    }

    private void InvalidatePublicCaches()
    {
        _cache.RemoveByTag(MetadataCacheKeys.AllMediaTag);
        _cache.RemoveByTag(MetadataCacheKeys.AllTvShowsTag);
        _cache.RemoveByTag(MetadataCacheKeys.AllCoursesTag);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Lock();
    }
}
