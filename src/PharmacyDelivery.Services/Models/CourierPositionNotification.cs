namespace PharmacyDelivery.Services.Models;

/// <summary>Courier position pushed to the customer tracking an order.</summary>
public sealed record CourierPositionNotification(
    Guid OrderId,
    Guid CustomerId,
    double Latitude,
    double Longitude,
    DateTimeOffset RecordedAt,
    DateTimeOffset EstimatedDeliveryAt);
