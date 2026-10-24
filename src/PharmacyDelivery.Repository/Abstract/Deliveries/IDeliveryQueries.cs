using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Read side of deliveries and courier availability.</summary>
public interface IDeliveryQueries
{
    /// <summary>The courier's assigned or in-progress delivery with pickup and drop-off points, or null.</summary>
    Task<CurrentDeliveryInfo?> GetCurrentForCourierAsync(Guid courierId, CancellationToken cancellationToken);

    /// <summary>Hot path of GPS ingestion (compiled query).</summary>
    Task<ActiveDeliveryRef?> GetActiveRefForCourierAsync(Guid courierId, CancellationToken cancellationToken);

    /// <summary>Subset of the given couriers that are available and have no active delivery.</summary>
    Task<IReadOnlyList<Guid>> GetAvailableCourierIdsAsync(IReadOnlyCollection<Guid> courierIds, CancellationToken cancellationToken);
}
