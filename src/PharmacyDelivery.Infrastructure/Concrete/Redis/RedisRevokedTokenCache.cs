using Microsoft.Extensions.Logging;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using StackExchange.Redis;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Denylist of revoked access tokens; entries live exactly until the token would expire anyway.</summary>
public sealed class RedisRevokedTokenCache(
    IConnectionMultiplexer redis,
    RedisKeys keys,
    IClock clock,
    ILogger<RedisRevokedTokenCache> logger) : IRevokedTokenCache
{
    public async Task MarkRevokedAsync(IReadOnlyCollection<RevokedToken> tokens, CancellationToken cancellationToken)
    {
        if (tokens.Count == 0)
            return;

        try
        {
            var db = redis.GetDatabase();
            var now = clock.UtcNow;
            await Task.WhenAll(tokens
                .Where(t => t.ExpiresAt > now)
                .Select(t => db.StringSetAsync(keys.RevokedJti(t.Jti), "1", t.ExpiresAt - now)));
        }
        catch (RedisException ex)
        {
            // The database remains the source of truth (fallback check in IsAccessTokenRevokedAsync).
            logger.LogWarning(ex, "Could not write {Count} revoked tokens to Redis", tokens.Count);
        }
    }

    public async Task<bool?> IsRevokedAsync(Guid jti, CancellationToken cancellationToken)
    {
        try
        {
            return await redis.GetDatabase().KeyExistsAsync(keys.RevokedJti(jti));
        }
        catch (RedisException ex)
        {
            logger.LogWarning(ex, "Redis unavailable for the revocation check; falling back to the database");
            return null;
        }
    }
}
