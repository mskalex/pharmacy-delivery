using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Shopping cart persisted in Redis with a TTL.</summary>
public interface ICartStore
{
    /// <summary>Stored cart of the user, or null.</summary>
    Task<Cart?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Stores the cart and refreshes its TTL.</summary>
    Task SaveAsync(Guid userId, Cart cart, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Deletes the cart.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
