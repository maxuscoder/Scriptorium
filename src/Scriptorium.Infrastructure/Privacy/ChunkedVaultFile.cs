using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Scriptorium.Infrastructure.Privacy;

/// <summary>Version 1: authenticated header, then independently authenticated fixed-size chunks.
/// Per-file HKDF keys permit deterministic, non-repeating 64-bit chunk nonces.</summary>
public static class ChunkedVaultFile
{
    public const int DefaultChunkSize = 1024 * 1024;
    internal const int HeaderSize = 56;
    internal const int TagSize = 16;
    internal static ReadOnlySpan<byte> Magic => "SCRPV001"u8;

    internal static byte[] FileKey(ReadOnlySpan<byte> masterKey, ReadOnlySpan<byte> header)
    {
        var result = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, result, header.Slice(20, 16), "Scriptorium/private/file/v1"u8);
        return result;
    }

    internal static void Nonce(Span<byte> nonce, ulong index)
    {
        nonce.Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], index);
    }

    internal static byte[] AssociatedData(byte[] header, ulong index)
    {
        var aad = new byte[HeaderSize + 8];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(HeaderSize), index);
        return aad;
    }

    public static async Task EncryptAsync(Stream source, Stream destination, ReadOnlyMemory<byte> masterKey,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default, int chunkSize = DefaultChunkSize, Guid? fileId = null)
    {
        if (chunkSize is < 1024 * 1024 or > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        var length = source.Length - source.Position;
        var header = new byte[HeaderSize];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), chunkSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12), length);
        RandomNumberGenerator.Fill(header.AsSpan(20, 16));
        (fileId ?? Guid.NewGuid()).TryWriteBytes(header.AsSpan(36, 16));
        var key = FileKey(masterKey.Span, header);
        var plain = new byte[chunkSize];
        var cipher = new byte[chunkSize];
        var tag = new byte[TagSize];
        var nonce = new byte[12];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            Nonce(nonce, ulong.MaxValue);
            aes.Encrypt(nonce.AsSpan(), ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, header);
            await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
            long completed = 0;
            ulong index = 0;
            while (completed < length)
            {
                var count = (int)Math.Min(chunkSize, length - completed);
                await source.ReadExactlyAsync(plain.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                Nonce(nonce, index);
                aes.Encrypt(nonce, plain.AsSpan(0, count), cipher.AsSpan(0, count), tag, AssociatedData(header, index));
                await destination.WriteAsync(cipher.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                completed += count;
                index++;
                progress?.Report(length == 0 ? 1 : (double)completed / length);
            }
            progress?.Report(1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}

/// <summary>Bounded two-buffer random-access decryption. No plaintext reaches the caller before authentication.</summary>
public sealed class VaultReadStream : Stream
{
    private readonly FileStream _file;
    private readonly byte[] _header;
    private readonly byte[] _key;
    private readonly byte[] _plain;
    private readonly byte[] _cipher;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationToken _session;
    private readonly int _chunkSize;
    private readonly long _length;
    private long _position;
    private long _cachedChunk = -1;
    private bool _disposed;

    public VaultReadStream(string path, ReadOnlySpan<byte> masterKey, CancellationToken session = default, Guid? expectedFileId = null)
    {
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.RandomAccess);
        _session = session;
        _header = new byte[ChunkedVaultFile.HeaderSize];
        _key = [];
        _plain = [];
        _cipher = [];
        try
        {
            _file.ReadExactly(_header);
            if (!_header.AsSpan(0, 8).SequenceEqual(ChunkedVaultFile.Magic)) throw new InvalidDataException("Unsupported vault format.");
            _key = ChunkedVaultFile.FileKey(masterKey, _header);
            Span<byte> tag = stackalloc byte[16];
            Span<byte> nonce = stackalloc byte[12];
            _file.ReadExactly(tag);
            ChunkedVaultFile.Nonce(nonce, ulong.MaxValue);
            using var aes = new AesGcm(_key, 16);
            aes.Decrypt(nonce, [], tag, [], _header);
            if (expectedFileId is { } expected && new Guid(_header.AsSpan(36, 16)) != expected)
                throw new CryptographicException("The encrypted file belongs to another private item.");
            _chunkSize = BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(8));
            _length = BinaryPrimitives.ReadInt64LittleEndian(_header.AsSpan(12));
            if (_chunkSize is < 1024 * 1024 or > 4 * 1024 * 1024 || _length < 0) throw new InvalidDataException("Invalid vault header.");
            var chunks = _length / _chunkSize + (_length % _chunkSize == 0 ? 0 : 1);
            if (_file.Length != checked(ChunkedVaultFile.HeaderSize + 16L + _length + chunks * 16)) throw new InvalidDataException("The encrypted file is incomplete or damaged.");
            _plain = new byte[_chunkSize];
            _cipher = new byte[_chunkSize + 16];
        }
        catch
        {
            _file.Dispose();
            CryptographicOperations.ZeroMemory(_key);
            throw;
        }
    }

    public override bool CanRead => !_disposed && !_session.IsCancellationRequested;
    public override bool CanSeek => CanRead;
    public override bool CanWrite => false;
    public override long Length { get { Check(); return _length; } }
    public override long Position { get { Check(); return _position; } set => Seek(value, SeekOrigin.Begin); }
    private void Check() { ObjectDisposedException.ThrowIf(_disposed, this); _session.ThrowIfCancellationRequested(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Check();
        using var linked = cancellationToken.CanBeCanceled && _session.CanBeCanceled && cancellationToken != _session
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _session) : null;
        var readToken = linked?.Token ?? (_session.CanBeCanceled ? _session : cancellationToken);
        await _gate.WaitAsync(readToken).ConfigureAwait(false);
        try
        {
            Check();
            readToken.ThrowIfCancellationRequested();
            var total = 0;
            while (total < buffer.Length && _position < _length)
            {
                Check();
                var index = _position / _chunkSize;
                var size = (int)Math.Min(_chunkSize, _length - index * _chunkSize);
                if (_cachedChunk != index)
                {
                    _cachedChunk = -1;
                    CryptographicOperations.ZeroMemory(_plain);
                    _file.Position = checked(ChunkedVaultFile.HeaderSize + 16L + index * ((long)_chunkSize + 16));
                    await _file.ReadExactlyAsync(_cipher.AsMemory(0, size + 16), readToken).ConfigureAwait(false);
                    var nonce = new byte[12];
                    ChunkedVaultFile.Nonce(nonce, (ulong)index);
                    using var aes = new AesGcm(_key, 16);
                    aes.Decrypt(nonce, _cipher.AsSpan(0, size), _cipher.AsSpan(size, 16), _plain.AsSpan(0, size),
                        ChunkedVaultFile.AssociatedData(_header, (ulong)index));
                    Check();
                    _cachedChunk = index;
                }
                var offset = (int)(_position % _chunkSize);
                var count = Math.Min(size - offset, buffer.Length - total);
                _plain.AsMemory(offset, count).CopyTo(buffer[total..]);
                _position += count;
                total += count;
            }
            return total;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(_plain);
            _cachedChunk = -1;
            throw;
        }
        finally { _gate.Release(); }
    }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override int Read(Span<byte> buffer)
    {
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length, 65536));
        try
        {
            var count = Read(rented, 0, Math.Min(buffer.Length, rented.Length));
            rented.AsSpan(0, count).CopyTo(buffer);
            return count;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        _gate.Wait();
        try
        {
            Check();
            var position = checked((origin switch { SeekOrigin.Begin => 0, SeekOrigin.Current => _position, SeekOrigin.End => _length, _ => throw new ArgumentOutOfRangeException(nameof(origin)) }) + offset);
            if (position < 0) throw new IOException("Cannot seek before the beginning.");
            return _position = position;
        }
        finally { _gate.Release(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Wait();
            try
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _file.Dispose();
                    CryptographicOperations.ZeroMemory(_key);
                    CryptographicOperations.ZeroMemory(_plain);
                }
            }
            finally { _gate.Release(); }
        }
        base.Dispose(disposing);
    }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
