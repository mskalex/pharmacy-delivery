using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;

namespace PharmacyDelivery.Domain.Services;

/// <summary>
/// Reserves all basket lines in one pharmacy on an "all or nothing" basis: availability of every line is checked
/// before the first mutation, so a failure never leaves a partially reserved basket in memory.
/// The caller must hold row locks on the stock rows (SELECT … FOR UPDATE).
/// </summary>
public static class StockAllocator
{
    public static void ReserveAll(
        IReadOnlyCollection<StockItem> lockedStock,
        IReadOnlyCollection<(ProductId ProductId, int Quantity)> lines,
        DateTimeOffset now)
    {
        var byProduct = lockedStock.ToDictionary(s => s.ProductId);
        var missing = lines
            .Where(l => !byProduct.TryGetValue(l.ProductId, out var stock) || !stock.CanReserve(l.Quantity))
            .Select(l => l.ProductId)
            .ToList();

        if (missing.Count > 0)
        {
            throw new DomainException(
                ErrorCodes.StockInsufficient,
                $"Insufficient stock for products: {string.Join(", ", missing)}.");
        }

        foreach (var (productId, quantity) in lines)
        {
            byProduct[productId].Reserve(quantity, now);
        }
    }
}
