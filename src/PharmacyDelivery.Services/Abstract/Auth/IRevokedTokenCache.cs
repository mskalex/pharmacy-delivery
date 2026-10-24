using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Denylist of revoked access tokens with TTL = token expiry.</summary>
public interface IRevokedTokenCache
{
    /// <summary>Adds revoked access tokens to the denylist until they expire.</summary>
    Task MarkRevokedAsync(IReadOnlyCollection<RevokedToken> tokens, CancellationToken cancellationToken);

    /// <summary>Whether the access token with this <c>jti</c> has been revoked.</summary>
    /// <returns>true/false, or null when the cache is unavailable (the caller falls back to the database).</returns>
    Task<bool?> IsRevokedAsync(Guid jti, CancellationToken cancellationToken);
}
