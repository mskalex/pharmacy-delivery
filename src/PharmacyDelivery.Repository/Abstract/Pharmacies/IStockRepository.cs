using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Access to stock rows of pharmacies, including row locking for reservations.</summary>
public interface IStockRepository
{
    /// <summary>Adds a stock row.</summary>
    void Add(StockItem item);

    /// <summary>
    /// Locks stock rows with <c>SELECT … FOR UPDATE ORDER BY product_id</c> under <c>lock_timeout</c>.
    /// The deterministic order prevents deadlocks between baskets sharing products.
    /// </summary>
    Task<IReadOnlyList<StockItem>> LockAsync(PharmacyId pharmacyId, IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken);

    /// <summary>All stock rows of a pharmacy without locks (input of stock synchronization).</summary>
    Task<IReadOnlyList<StockItem>> GetForPharmacyAsync(PharmacyId pharmacyId, CancellationToken cancellationToken);
}
