using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Fake feed of pharmacy stock counts: replenishes products that are about to run out (as a wholesaler delivery
/// would). Real pharmacy integrations plug in behind <see cref="IPharmacyStockFeed"/>.
/// </summary>
public sealed class FakePharmacyStockFeed : IPharmacyStockFeed
{
    private const int LowStockThreshold = 2;
    private const int RestockQuantity = 20;

    public Task<IReadOnlyDictionary<Guid, int>> GetStockCountsAsync(
        Guid pharmacyId,
        IReadOnlyCollection<(Guid ProductId, int OnHand, int Reserved)> current,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, int> counts = current
            .Where(c => c.OnHand - c.Reserved < LowStockThreshold)
            .ToDictionary(c => c.ProductId, c => c.OnHand + RestockQuantity);
        return Task.FromResult(counts);
    }
}
