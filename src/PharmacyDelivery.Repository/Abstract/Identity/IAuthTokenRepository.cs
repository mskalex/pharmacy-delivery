using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Persistence of sessions and tokens. Tokens are stored only as SHA-256 hashes.</summary>
public interface IAuthTokenRepository
{
    /// <summary>Adds a new device session (one chain of rotating refresh tokens).</summary>
    void AddSession(AuthSession session);

    /// <summary>Adds a refresh token (hash only).</summary>
    void AddRefreshToken(RefreshToken token);

    /// <summary>Registers an issued access token by its <c>jti</c>, so it can be revoked before it expires.</summary>
    void AddAccessToken(AccessToken token);

    /// <summary>Remembers a password hash for the password reuse check.</summary>
    void AddPasswordHistory(PasswordHistoryEntry entry);

    /// <summary>Finds a refresh token by the hash of its value (tracked, for rotation).</summary>
    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Loads a session (tracked).</summary>
    Task<AuthSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Revokes sessions, their refresh tokens and access tokens; returns revoked access tokens (for the Redis denylist).</summary>
    Task<IReadOnlyList<RevokedToken>> RevokeSessionsAsync(
        UserId userId,
        Guid? onlySessionId,
        Guid? exceptSessionId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Database fallback of the revocation check when Redis is unavailable.</summary>
    Task<bool> IsAccessTokenRevokedAsync(Guid jti, CancellationToken cancellationToken);

    /// <summary>Hashes of the most recent passwords of a user, newest first.</summary>
    Task<IReadOnlyList<string>> GetRecentPasswordHashesAsync(UserId userId, int count, CancellationToken cancellationToken);

    /// <summary>Deletes tokens and revoked sessions that expired before the given moment; returns the number of deleted rows.</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset expiredBefore, int batchSize, CancellationToken cancellationToken);
}
