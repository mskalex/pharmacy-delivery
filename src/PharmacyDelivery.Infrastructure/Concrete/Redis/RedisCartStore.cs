using System.Text.Json;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;
using StackExchange.Redis;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Cart as JSON with a schema version; TTL is refreshed on every write.</summary>
public sealed class RedisCartStore(IConnectionMultiplexer redis, RedisKeys keys) : ICartStore
{
    public async Task<Cart?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase().StringGetAsync(keys.Cart(userId));
        if (value.IsNullOrEmpty)
            return null;

        var cart = JsonSerializer.Deserialize<Cart>(value.ToString(), PharmacyDeliveryJsonOptions.Tolerant);
        return cart is null ? null : Migrate(cart);
    }

    public Task SaveAsync(Guid userId, Cart cart, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cart.SchemaVersion = Cart.CurrentSchemaVersion;
        var json = JsonSerializer.Serialize(cart, PharmacyDeliveryJsonOptions.Tolerant);
        return redis.GetDatabase().StringSetAsync(keys.Cart(userId), json, ttl);
    }

    public Task DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        redis.GetDatabase().KeyDeleteAsync(keys.Cart(userId));

    /// <summary>Upgrades older stored formats on read (only version 1 exists so far).</summary>
    private static Cart Migrate(Cart cart) => cart.SchemaVersion switch
    {
        Cart.CurrentSchemaVersion => cart,
        _ => new Cart { SchemaVersion = Cart.CurrentSchemaVersion, Items = cart.Items, UpdatedAt = cart.UpdatedAt },
    };
}
