using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Write-side access to deliveries.</summary>
public interface IDeliveryRepository
{
    /// <summary>Adds a new delivery (unique partial indexes guard one active delivery per courier and per order).</summary>
    void Add(Delivery delivery);

    /// <summary>Loads a delivery (tracked).</summary>
    Task<Delivery?> GetAsync(DeliveryId id, CancellationToken cancellationToken);

    /// <summary>The courier's assigned or in-progress delivery (tracked), or null.</summary>
    Task<Delivery?> GetActiveForCourierAsync(CourierId courierId, CancellationToken cancellationToken);

    /// <summary>The order's assigned or in-progress delivery (tracked), or null.</summary>
    Task<Delivery?> GetActiveForOrderAsync(OrderId orderId, CancellationToken cancellationToken);
}
