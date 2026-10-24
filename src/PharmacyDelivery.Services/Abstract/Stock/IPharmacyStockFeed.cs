namespace PharmacyDelivery.Services.Abstract;

/// <summary>Stock counts reported by pharmacy systems.</summary>
public interface IPharmacyStockFeed
{
    /// <summary>Stock counts reported by the pharmacy system for changed products (product id → on-hand quantity).</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetStockCountsAsync(Guid pharmacyId,
        IReadOnlyCollection<(Guid ProductId, int OnHand, int Reserved)> current, CancellationToken cancellationToken);
}
