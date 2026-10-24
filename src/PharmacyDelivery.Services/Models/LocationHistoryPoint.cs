namespace PharmacyDelivery.Services.Models;

/// <summary>Point queued for the history writer.</summary>
public sealed record LocationHistoryPoint(
    Guid CourierId,
    Guid? DeliveryId,
    double Latitude,
    double Longitude,
    double? SpeedKmh,
    DateTimeOffset RecordedAt);
