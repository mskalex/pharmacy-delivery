using System.Security.Cryptography;
using System.Text;

namespace PharmacyDelivery.Common.Security;

/// <summary>Hashing and generation of opaque tokens (refresh tokens). Only hashes are ever persisted.</summary>
public static class TokenHashing
{
    /// <summary>Returns the lowercase hex SHA-256 of the UTF-8 bytes of <paramref name="value"/>.</summary>
    public static string Sha256Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Generates a URL-safe random token with <paramref name="bytes"/> bytes of entropy (default 256 bits).</summary>
    public static string GenerateOpaqueToken(int bytes = 32)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
