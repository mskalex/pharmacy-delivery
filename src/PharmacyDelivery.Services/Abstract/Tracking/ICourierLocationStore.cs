using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Latest courier positions (Redis GEO index + hash). Source for dispatch and live tracking.</summary>
public interface ICourierLocationStore
{
    /// <summary>Stores the latest position (GEO index + timestamp).</summary>
    Task UpdateAsync(CourierPosition position, CancellationToken cancellationToken);

    /// <summary>Latest position of a courier, or null.</summary>
    Task<CourierPosition?> GetAsync(Guid courierId, CancellationToken cancellationToken);

    /// <summary>Couriers nearest to the point within the radius, closest first, with the age of their position.</summary>
    Task<IReadOnlyList<NearbyCourier>> FindNearestAsync(GeoPoint point, double radiusKm, int count, CancellationToken cancellationToken);
}
