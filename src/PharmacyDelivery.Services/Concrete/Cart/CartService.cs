using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Cart is stored in Redis and contains only product ids and quantities. Prices are always taken from the catalog
/// when the cart is read, so a price change is visible immediately and checkout snapshots the current price.
/// </summary>
public sealed class CartService(
    ICartStore store,
    ICatalogQueries catalog,
    IClock clock,
    IOptions<CheckoutOptions> options) : ICartService
{
    private readonly CheckoutOptions _options = options.Value;

    public async Task<CartView> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cart = await store.GetAsync(userId, cancellationToken) ?? new Cart();
        return await BuildViewAsync(cart, cancellationToken);
    }

    public async Task<CartView> SetItemAsync(Guid userId, Guid productId, int quantity, CancellationToken cancellationToken)
    {
        if (quantity < 1 || quantity > _options.MaxQuantityPerItem)
        {
            throw new BusinessRuleException(
                ErrorCodes.CartQuantityOutOfRange,
                $"Quantity must be between 1 and {_options.MaxQuantityPerItem}.");
        }

        var product = await catalog.GetAsync(productId, cancellationToken)
                      ?? throw new NotFoundException(ErrorCodes.ProductNotFound, $"Product {productId} was not found.");
        if (!product.IsActive)
            throw new BusinessRuleException(ErrorCodes.ProductInactive, $"Product {product.Name} is no longer sold.");

        var cart = await store.GetAsync(userId, cancellationToken) ?? new Cart();
        var item = cart.Items.Find(i => i.ProductId == productId);
        if (item is null)
        {
            if (cart.Items.Count >= _options.MaxCartItems)
            {
                throw new BusinessRuleException(
                    ErrorCodes.CartTooManyItems,
                    $"A cart may contain at most {_options.MaxCartItems} different products.");
            }

            cart.Items.Add(new CartItem { ProductId = productId, Quantity = quantity });
        }
        else
        {
            item.Quantity = quantity;
        }

        await SaveAsync(userId, cart, cancellationToken);
        return await BuildViewAsync(cart, cancellationToken);
    }

    public async Task<CartView> RemoveItemAsync(Guid userId, Guid productId, CancellationToken cancellationToken)
    {
        var cart = await store.GetAsync(userId, cancellationToken) ?? new Cart();
        cart.Items.RemoveAll(i => i.ProductId == productId);
        await SaveAsync(userId, cart, cancellationToken);
        return await BuildViewAsync(cart, cancellationToken);
    }

    public Task ClearAsync(Guid userId, CancellationToken cancellationToken) => store.DeleteAsync(userId, cancellationToken);

    private Task SaveAsync(Guid userId, Cart cart, CancellationToken cancellationToken)
    {
        cart.UpdatedAt = clock.UtcNow;
        return store.SaveAsync(userId, cart, TimeSpan.FromHours(_options.CartTtlHours), cancellationToken);
    }

    private async Task<CartView> BuildViewAsync(Cart cart, CancellationToken cancellationToken)
    {
        if (cart.Items.Count == 0)
            return new CartView([], 0, Money.Usd, null);

        var products = (await catalog.GetManyAsync(cart.Items.Select(i => i.ProductId).ToList(), cancellationToken))
            .ToDictionary(p => p.Id);

        var lines = cart.Items
            .Select(i =>
            {
                var found = products.TryGetValue(i.ProductId, out var product);
                var price = found ? product!.Price : 0;
                return new CartLineView(
                    i.ProductId,
                    found ? product!.Name : "(unavailable)",
                    price,
                    i.Quantity,
                    price * i.Quantity,
                    found && product!.IsActive);
            })
            .ToList();

        return new CartView(
            lines,
            lines.Where(l => l.IsAvailable).Sum(l => l.LineTotal),
            Money.Usd,
            cart.UpdatedAt.AddHours(_options.CartTtlHours));
    }
}
