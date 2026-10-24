using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Use cases of the courier app.</summary>
public interface ICourierDeliveryService
{
    /// <summary>The courier's current delivery, or null when idle.</summary>
    Task<CurrentDeliveryInfo?> GetCurrentAsync(Guid courierId, CancellationToken cancellationToken);

    /// <summary>Confirms pickup at the pharmacy: the order goes InDelivery and tracking becomes visible to the customer.</summary>
    Task PickUpAsync(Guid courierId, Guid deliveryId, CancellationToken cancellationToken);

    /// <summary>Confirms delivery: the order is Delivered and the courier becomes available.</summary>
    Task DeliverAsync(Guid courierId, Guid deliveryId, CancellationToken cancellationToken);
}
