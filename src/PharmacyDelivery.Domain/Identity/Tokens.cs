using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Identity;

/// <summary>Device session; owns one chain (family) of rotating refresh tokens.</summary>
public sealed class AuthSession : Entity<Guid>
{
    private AuthSession()
    {
    }

    public UserId UserId { get; private set; }

    public string? UserAgent { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? RevokeReason { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static AuthSession Start(UserId userId, string? userAgent, string? ipAddress, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            UserAgent = userAgent is { Length: > 256 } ? userAgent[..256] : userAgent,
            IpAddress = ipAddress,
            CreatedAt = now,
            LastUsedAt = now,
        };

    public void Touch(DateTimeOffset now) => LastUsedAt = now;

    public void Revoke(string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
            return;

        RevokedAt = now;
        RevokeReason = reason;
    }
}

/// <summary>Refresh token. Only the SHA-256 hash is stored; each use rotates the token.</summary>
public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken()
    {
    }

    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsUsable(DateTimeOffset now) => UsedAt is null && RevokedAt is null && ExpiresAt > now;

    public static RefreshToken Issue(Guid sessionId, string tokenHash, DateTimeOffset now, TimeSpan lifetime) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SessionId = sessionId,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        };

    public void MarkUsed(Guid replacedById, DateTimeOffset now)
    {
        UsedAt = now;
        ReplacedById = replacedById;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>
/// Issued access token (JWT) registered by its <c>jti</c> so that it can be revoked immediately, before <c>exp</c>.
/// </summary>
public sealed class AccessToken
{
    private AccessToken()
    {
    }

    public Guid Jti { get; private set; }

    public Guid SessionId { get; private set; }

    public UserId UserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static AccessToken Register(Guid jti, Guid sessionId, UserId userId, DateTimeOffset now, DateTimeOffset expiresAt) =>
        new()
        {
            Jti = jti,
            SessionId = sessionId,
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>Previous password hashes; used to prevent reusing the last N passwords.</summary>
public sealed class PasswordHistoryEntry : Entity<Guid>
{
    private PasswordHistoryEntry()
    {
    }

    public UserId UserId { get; private set; }

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static PasswordHistoryEntry Create(UserId userId, string passwordHash, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(), UserId = userId, PasswordHash = passwordHash, CreatedAt = now };
}
