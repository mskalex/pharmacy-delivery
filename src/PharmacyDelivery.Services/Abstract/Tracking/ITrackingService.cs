using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Real-time courier tracking: GPS ingestion, snapshots and subscription authorization.</summary>
public interface ITrackingService
{
    /// <summary>Validates and stores a batch of GPS points; pushes the position to the customer while the order is InDelivery.</summary>
    Task<LocationIngestResult> IngestAsync(Guid courierId, IReadOnlyList<LocationPointInput> points, CancellationToken cancellationToken);

    /// <summary>Current tracking state of an order (courier position only while InDelivery).</summary>
    Task<TrackingSnapshot> GetSnapshotAsync(Guid orderId, ICurrentUser user, CancellationToken cancellationToken);

    /// <summary>Authorization of a SignalR subscription: only the customer of the order (or an admin).</summary>
    Task<bool> CanSubscribeAsync(Guid orderId, ICurrentUser user, CancellationToken cancellationToken);
}
