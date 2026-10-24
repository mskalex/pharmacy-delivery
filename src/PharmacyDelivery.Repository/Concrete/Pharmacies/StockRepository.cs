using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class StockRepository(PharmacyDeliveryDbContext db) : IStockRepository
{
    public void Add(StockItem item) => db.StockItems.Add(item);

    public async Task<IReadOnlyList<StockItem>> LockAsync(
        PharmacyId pharmacyId,
        IReadOnlyCollection<ProductId> productIds,
        CancellationToken cancellationToken)
    {
        var ids = productIds.Select(p => p.Value).Distinct().Order().ToArray();
        if (ids.Length == 0)
            return [];

        // ORDER BY + FOR UPDATE: rows are locked in product id order, so baskets sharing products never deadlock.
        // Waiting is bounded by lock_timeout (set by the unit of work) -> 55P03 -> 409 stock.busy.
        return await db.StockItems
            .FromSql(
                $"SELECT * FROM stock_items WHERE pharmacy_id = {pharmacyId.Value} AND product_id = ANY({ids}) ORDER BY product_id FOR UPDATE")
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockItem>> GetForPharmacyAsync(PharmacyId pharmacyId, CancellationToken cancellationToken) =>
        await db.StockItems.AsNoTracking()
            .Where(s => s.PharmacyId == pharmacyId)
            .ToListAsync(cancellationToken);
}
