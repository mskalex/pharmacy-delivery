using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PharmacyDelivery.Common.Security;

/// <summary>
/// HMAC-SHA256 signatures for webhooks. The signed string is <c>"{timestamp}.{payload}"</c> so that a captured
/// request cannot be replayed with a different timestamp; verification uses a constant-time comparison.
/// </summary>
public static class HmacSignature
{
    public static string Compute(string secret, long unixTimestamp, string payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(payload);

        var data = Encoding.UTF8.GetBytes(unixTimestamp.ToString(CultureInfo.InvariantCulture) + "." + payload);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data);
        return Convert.ToHexStringLower(mac);
    }

    /// <summary>
    /// Verifies the signature and the timestamp window. Returns <c>false</c> for a wrong signature, a malformed
    /// signature or a timestamp outside <paramref name="tolerance"/> from <paramref name="now"/>.
    /// </summary>
    public static bool Verify(
        string secret,
        long unixTimestamp,
        string payload,
        string? signature,
        DateTimeOffset now,
        TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        var age = now - DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        if (age.Duration() > tolerance)
            return false;

        var expected = Encoding.ASCII.GetBytes(Compute(secret, unixTimestamp, payload));
        var actual = Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
