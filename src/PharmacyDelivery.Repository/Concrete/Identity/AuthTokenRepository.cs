using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class AuthTokenRepository(PharmacyDeliveryDbContext db) : IAuthTokenRepository
{
    public void AddSession(AuthSession session) => db.AuthSessions.Add(session);

    public void AddRefreshToken(RefreshToken token) => db.RefreshTokens.Add(token);

    public void AddAccessToken(AccessToken token) => db.AccessTokens.Add(token);

    public void AddPasswordHistory(PasswordHistoryEntry entry) => db.PasswordHistory.Add(entry);

    public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<AuthSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<RevokedToken>> RevokeSessionsAsync(
        UserId userId,
        Guid? onlySessionId,
        Guid? exceptSessionId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Tracked updates (not ExecuteUpdate) so the audit log records every revoked session and token.
        var sessions = await db.AuthSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .Where(s => onlySessionId == null || s.Id == onlySessionId)
            .Where(s => exceptSessionId == null || s.Id != exceptSessionId)
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
            return [];

        var sessionIds = sessions.Select(s => s.Id).ToList();
        foreach (var session in sessions)
            session.Revoke(reason, now);

        var refreshTokens = await db.RefreshTokens
            .Where(t => sessionIds.Contains(t.SessionId) && t.RevokedAt == null && t.UsedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in refreshTokens)
            token.Revoke(now);

        var accessTokens = await db.AccessTokens
            .Where(t => sessionIds.Contains(t.SessionId) && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var token in accessTokens)
            token.Revoke(now);

        return accessTokens.Select(t => new RevokedToken(t.Jti, t.ExpiresAt)).ToList();
    }

    public Task<bool> IsAccessTokenRevokedAsync(Guid jti, CancellationToken cancellationToken) =>
        db.AccessTokens.AsNoTracking().AnyAsync(t => t.Jti == jti && t.RevokedAt != null, cancellationToken);

    public async Task<IReadOnlyList<string>> GetRecentPasswordHashesAsync(UserId userId, int count, CancellationToken cancellationToken) =>
        await db.PasswordHistory.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(count)
            .Select(p => p.PasswordHash)
            .ToListAsync(cancellationToken);

    public async Task<int> DeleteExpiredAsync(DateTimeOffset expiredBefore, int batchSize, CancellationToken cancellationToken)
    {
        // Bulk deletes are fine here: retention cleanup is not an audited user action.
        var access = await db.AccessTokens.Where(t => t.ExpiresAt < expiredBefore).ExecuteDeleteAsync(cancellationToken);
        var refresh = await db.RefreshTokens.Where(t => t.ExpiresAt < expiredBefore).ExecuteDeleteAsync(cancellationToken);
        var sessions = await db.AuthSessions
            .Where(s => s.RevokedAt != null && s.RevokedAt < expiredBefore)
            .ExecuteDeleteAsync(cancellationToken);
        return access + refresh + sessions;
    }
}
