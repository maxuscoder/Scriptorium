using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Scriptorium.Infrastructure.Privacy;
using Xunit;
using Xunit.Abstractions;

namespace Scriptorium.Tests.Infrastructure;

public sealed class VaultReadStreamTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Chunk_sizes_support_sequential_reads_and_random_seeking_with_bounded_buffers(int mebibytes)
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "encrypted.spv");
        var key = RandomNumberGenerator.GetBytes(32);
        var content = RandomNumberGenerator.GetBytes(24 * 1024 * 1024 + 19);
        try
        {
            var timer = Stopwatch.StartNew();
            await using (var destination = File.Create(path))
                await ChunkedVaultFile.EncryptAsync(new MemoryStream(content), destination, key, chunkSize: mebibytes * 1024 * 1024);
            var encryptionSeconds = timer.Elapsed.TotalSeconds;
            timer.Restart();
            using var stream = new VaultReadStream(path, key);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536];
            int count;
            while ((count = await stream.ReadAsync(buffer)) != 0) hash.AppendData(buffer, 0, count);
            Assert.Equal(SHA256.HashData(content), hash.GetHashAndReset());
            var decryptionSeconds = timer.Elapsed.TotalSeconds;
            timer.Restart();
            var random = new Random(127);
            for (var i = 0; i < 50; i++)
            {
                var offset = random.Next(content.Length - buffer.Length);
                stream.Seek(offset, SeekOrigin.Begin);
                await stream.ReadExactlyAsync(buffer);
                Assert.True(buffer.AsSpan().SequenceEqual(content.AsSpan(offset, buffer.Length)));
            }
            output.WriteLine($"{mebibytes} MiB: encrypt {24 / encryptionSeconds:F0} MiB/s; decrypt {24 / decryptionSeconds:F0} MiB/s; random 64 KiB reads {timer.Elapsed.TotalMilliseconds / 50:F2} ms/read");
            Assert.Equal(content.Length, stream.Seek(0, SeekOrigin.End));
            Assert.Equal(0, await stream.ReadAsync(buffer));
            Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
        }
        finally { CryptographicOperations.ZeroMemory(key); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Header_identity_key_and_file_length_are_authenticated()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "encrypted.spv");
        var key = RandomNumberGenerator.GetBytes(32);
        var id = Guid.NewGuid();
        try
        {
            await using (var destination = File.Create(path))
                await ChunkedVaultFile.EncryptAsync(new MemoryStream([1, 2, 3]), destination, key, fileId: id);
            Assert.Throws<AuthenticationTagMismatchException>(() => new VaultReadStream(path, RandomNumberGenerator.GetBytes(32)));
            Assert.Throws<CryptographicException>(() => new VaultReadStream(path, key, expectedFileId: Guid.NewGuid()));
            var original = await File.ReadAllBytesAsync(path);
            var changed = original.ToArray();
            changed[12] ^= 1;
            await File.WriteAllBytesAsync(path, changed);
            Assert.Throws<AuthenticationTagMismatchException>(() => new VaultReadStream(path, key));
            await File.WriteAllBytesAsync(path, original[..^1]);
            Assert.Throws<InvalidDataException>(() => new VaultReadStream(path, key));
            await File.WriteAllBytesAsync(path, [.. original, 0]);
            Assert.Throws<InvalidDataException>(() => new VaultReadStream(path, key));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Corruption_and_chunk_reordering_never_release_unauthenticated_plaintext()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "encrypted.spv");
        var key = RandomNumberGenerator.GetBytes(32);
        const int chunk = 1024 * 1024;
        try
        {
            await using (var destination = File.Create(path))
                await ChunkedVaultFile.EncryptAsync(new MemoryStream(RandomNumberGenerator.GetBytes(chunk * 2)), destination, key);
            var content = await File.ReadAllBytesAsync(path);
            var first = content.AsSpan(72, chunk + 16).ToArray();
            content.AsSpan(72 + chunk + 16, chunk + 16).CopyTo(content.AsSpan(72));
            first.CopyTo(content, 72 + chunk + 16);
            await File.WriteAllBytesAsync(path, content);
            using var stream = new VaultReadStream(path, key);
            var output = Enumerable.Repeat((byte)0x42, 1024).ToArray();
            await Assert.ThrowsAsync<AuthenticationTagMismatchException>(async () => await stream.ReadExactlyAsync(output));
            Assert.All(output, b => Assert.Equal(0x42, b));
            Assert.Equal(0, stream.Position);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Empty_files_and_revoked_sessions_are_handled_without_plaintext_temporary_files()
    {
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "encrypted.spv");
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            await using (var destination = File.Create(path)) await ChunkedVaultFile.EncryptAsync(new MemoryStream(), destination, key);
            using var session = new CancellationTokenSource();
            using var stream = new VaultReadStream(path, key, session.Token);
            Assert.Equal(0, stream.Length);
            Assert.Equal(-1, stream.ReadByte());
            session.Cancel();
            Assert.False(stream.CanRead);
            Assert.Throws<OperationCanceledException>(() => stream.ReadByte());
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Seeks_beyond_100_GiB_use_64_bit_offsets_and_authenticate_only_the_requested_chunks()
    {
        // A sparse vault exercises real file offsets without writing 100 GiB of test data.
        var directory = CreateDirectory();
        var path = Path.Combine(directory, "large.spv");
        var master = RandomNumberGenerator.GetBytes(32);
        const int chunkSize = 1024 * 1024;
        const long length = 101L * 1024 * 1024 * 1024 + 79;
        var header = new byte[56];
        "SCRPV001"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), chunkSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12), length);
        RandomNumberGenerator.Fill(header.AsSpan(20, 16));
        Guid.NewGuid().TryWriteBytes(header.AsSpan(36, 16));
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, master, key, header.AsSpan(20, 16), "Scriptorium/private/file/v1"u8);
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (OperatingSystem.IsWindows())
                    Assert.True(DeviceIoControl(file.SafeFileHandle, 0x000900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero)); // FSCTL_SET_SPARSE
                var chunks = length / chunkSize + 1;
                file.SetLength(72 + length + chunks * 16);
                file.Position = 0;
                file.Write(header);
                using var aes = new AesGcm(key, 16);
                var nonce = new byte[12];
                BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(4), ulong.MaxValue);
                var tag = new byte[16];
                aes.Encrypt(nonce.AsSpan(), ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, header);
                file.Write(tag);
                foreach (var index in new[] { 0L, 50L * 1024, 100L * 1024, length / chunkSize })
                {
                    var size = (int)Math.Min(chunkSize, length - index * chunkSize);
                    var plaintext = Enumerable.Repeat((byte)(index % 251), size).ToArray();
                    var ciphertext = new byte[size];
                    BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(4), (ulong)index);
                    var aad = new byte[64];
                    header.CopyTo(aad, 0);
                    BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(56), (ulong)index);
                    aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
                    file.Position = 72 + index * (chunkSize + 16L);
                    file.Write(ciphertext);
                    file.Write(tag);
                }
            }
            using var stream = new VaultReadStream(path, master);
            Assert.Equal(length, stream.Length);
            var buffer = new byte[32];
            foreach (var index in new[] { 100L * 1024, 0, length / chunkSize, 50L * 1024 })
            {
                Assert.Equal(index * chunkSize, stream.Seek(index * chunkSize, SeekOrigin.Begin));
                await stream.ReadExactlyAsync(buffer);
                Assert.All(buffer, b => Assert.Equal((byte)(index % 251), b));
            }
            stream.Seek(-7, SeekOrigin.End);
            Assert.Equal(7, await stream.ReadAsync(buffer));
            stream.Seek(chunkSize, SeekOrigin.Begin); // An unwritten sparse chunk has no authentic tag.
            await Assert.ThrowsAsync<AuthenticationTagMismatchException>(async () => await stream.ReadExactlyAsync(buffer));
        }
        finally { CryptographicOperations.ZeroMemory(key); Directory.Delete(directory, true); }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ScriptoriumVaultTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint bytesReturned, IntPtr overlapped);
}
