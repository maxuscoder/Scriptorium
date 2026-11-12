using System.Security.Cryptography;
using System.Text;

namespace Scriptorium.Infrastructure.Privacy;

public sealed class PrivateLibraryConfiguration
{
    public int Id { get; set; } = 1;
    public int Mode { get; set; }
    public int AutoLockMinutes { get; set; } = 15;
    public byte[] Salt { get; set; } = [];
    public byte[] WrappedKey { get; set; } = [];
}

public sealed class PrivateMediaRecord
{
    public Guid Id { get; set; }
    public byte[] Payload { get; set; } = [];
}

public sealed class PrivateSource
{
    public required string PathHash { get; set; }
    public Guid PrivateMediaId { get; set; }
}

public sealed record PrivateVaultOptions(string DirectoryPath);

public static class PrivatePathHash
{
    public static string Compute(string path) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant())));
}
