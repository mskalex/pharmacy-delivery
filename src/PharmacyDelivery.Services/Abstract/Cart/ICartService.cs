using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Shopping cart use cases (cart stored in Redis, prices always from the catalog).</summary>
public interface ICartService
{
    /// <summary>Cart with current prices and availability of each line.</summary>
    Task<CartView> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Adds a product or sets its quantity (limits from Checkout options).</summary>
    Task<CartView> SetItemAsync(Guid userId, Guid productId, int quantity, CancellationToken cancellationToken);

    /// <summary>Removes a product from the cart.</summary>
    Task<CartView> RemoveItemAsync(Guid userId, Guid productId, CancellationToken cancellationToken);

    /// <summary>Empties the cart.</summary>
    Task ClearAsync(Guid userId, CancellationToken cancellationToken);
}
