using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Finds and ranks pharmacies able to fulfil a whole basket for a delivery address.</summary>
public interface IPharmacySelector
{
    /// <summary>
    /// Pharmacies that are open now, within the delivery radius and able to fulfil every line, ranked by expected
    /// delivery time. The read is lock-free and may be slightly stale; the reservation re-checks under row locks.
    /// </summary>
    Task<PharmacySelection> SelectAsync(
        GeoPoint destination,
        IReadOnlyCollection<(ProductId ProductId, int Quantity)> lines,
        IReadOnlyCollection<Guid> excludedPharmacyIds,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
