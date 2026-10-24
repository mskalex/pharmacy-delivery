using Microsoft.Extensions.Options;
using PharmacyDelivery.Infrastructure.Options;
using StackExchange.Redis;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Key layout of everything the application stores in Redis (all keys share one prefix).</summary>
public sealed class RedisKeys(IOptions<RedisOptions> options)
{
    public string Prefix { get; } = options.Value.KeyPrefix;

    public RedisKey Cart(Guid userId) => Prefix + "cart:" + userId.ToString("N");

    public RedisKey CourierGeo => Prefix + "couriers:geo";

    public RedisKey Courier(Guid courierId) => Prefix + "courier:" + courierId.ToString("N");

    public RedisKey Lock(string resource) => Prefix + "lock:" + resource;

    public RedisKey RevokedJti(Guid jti) => Prefix + "revoked-jti:" + jti.ToString("N");

    public RedisKey SeedResetMarker => Prefix + "seed:reset-done";
}
