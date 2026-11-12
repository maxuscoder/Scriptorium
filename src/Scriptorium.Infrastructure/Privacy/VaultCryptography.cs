using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace Scriptorium.Infrastructure.Privacy;

internal static class VaultCryptography
{
    // Fixed, versioned parameters avoid accepting attacker-controlled KDF work factors.
    public static async Task<byte[]> DeriveAsync(ReadOnlyMemory<byte> password, byte[] salt)
    {
        var bytes = password.ToArray();
        try
        {
            using var argon = new Argon2id(bytes) { Salt = salt, MemorySize = 65536, Iterations = 3, DegreeOfParallelism = 4 };
            return await argon.GetBytesAsync(32).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static byte[] Seal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> aad)
    {
        var result = new byte[checked(28 + plaintext.Length)];
        RandomNumberGenerator.Fill(result.AsSpan(0, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(0, 12), plaintext, result.AsSpan(28), result.AsSpan(12, 16), aad);
        return result;
    }

    public static byte[] Unseal(ReadOnlySpan<byte> key, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> aad)
    {
        if (payload.Length < 28) throw new CryptographicException("The private library data is damaged.");
        var result = new byte[payload.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(payload[..12], payload[28..], payload.Slice(12, 16), result, aad);
            return result;
        }
        catch { CryptographicOperations.ZeroMemory(result); throw; }
    }
}
