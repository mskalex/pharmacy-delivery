using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Write-side access to couriers.</summary>
public interface ICourierRepository
{
    /// <summary>Loads a courier (tracked; optimistic concurrency via xmin).</summary>
    Task<Courier?> GetAsync(CourierId id, CancellationToken cancellationToken);
}
