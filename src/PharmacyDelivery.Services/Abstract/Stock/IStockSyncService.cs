namespace PharmacyDelivery.Services.Abstract;

/// <summary>Synchronization of stock counts with pharmacy systems.</summary>
public interface IStockSyncService
{
    /// <summary>Applies stock counts reported by one pharmacy's system. Returns the number of changed rows.</summary>
    Task<int> SyncPharmacyAsync(Guid pharmacyId, CancellationToken cancellationToken);

    /// <summary>Pharmacies to synchronize (all active ones).</summary>
    Task<IReadOnlyList<Guid>> GetPharmacyIdsAsync(CancellationToken cancellationToken);
}
