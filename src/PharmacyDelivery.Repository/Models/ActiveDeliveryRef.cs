using PharmacyDelivery.Domain.Deliveries;

namespace PharmacyDelivery.Repository.Models;

/// <summary>Active delivery of a courier as needed by the tracking hot path.</summary>
public sealed record ActiveDeliveryRef(
    Guid DeliveryId,
    Guid OrderId,
    Guid CustomerId,
    DeliveryStatus Status,
    double DestinationLatitude,
    double DestinationLongitude);
