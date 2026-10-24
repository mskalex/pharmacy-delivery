using Microsoft.Extensions.Logging;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Synchronization with pharmacy systems. One call handles one pharmacy in one transaction; the background job
/// runs pharmacies in parallel (Parallel.ForEachAsync, one DI scope and DbContext per pharmacy).
/// </summary>
public sealed class StockSyncService(
    IStockRepository stock,
    IPharmacyRepository pharmacies,
    IPharmacyStockFeed feed,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<StockSyncService> logger) : IStockSyncService
{
    public async Task<IReadOnlyList<Guid>> GetPharmacyIdsAsync(CancellationToken cancellationToken) =>
        (await pharmacies.GetActiveIdsAsync(cancellationToken)).Select(id => id.Value).ToList();

    public Task<int> SyncPharmacyAsync(Guid pharmacyId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var id = new PharmacyId(pharmacyId);
                var current = await stock.GetForPharmacyAsync(id, ct);
                var counts = await feed.GetStockCountsAsync(
                    pharmacyId,
                    current.Select(s => (s.ProductId.Value, s.OnHand, s.Reserved)).ToList(),
                    ct);
                if (counts.Count == 0)
                    return 0;

                // Same lock as reservations (FOR UPDATE ordered by product id), so a sync never races a checkout.
                var locked = await stock.LockAsync(id, counts.Keys.Select(k => new ProductId(k)).ToList(), ct);
                var now = clock.UtcNow;
                var changed = 0;
                foreach (var item in locked)
                {
                    var counted = counts[item.ProductId.Value];
                    if (counted == item.OnHand)
                        continue;

                    var deficit = item.ApplyStockCount(counted, now);
                    changed++;
                    if (deficit > 0)
                    {
                        logger.LogWarning(
                            "Stock conflict in pharmacy {PharmacyId}: product {ProductId} counted {Counted} but {Reserved} reserved",
                            pharmacyId,
                            item.ProductId,
                            counted,
                            item.Reserved);
                    }
                }

                await unitOfWork.SaveChangesAsync(ct);
                return changed;
            },
            cancellationToken);
}
