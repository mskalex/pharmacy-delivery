namespace PharmacyDelivery.Domain.Tracking;

/// <summary>
/// GPS point in the courier location history (append-only, partitioned by day, retained for a limited time).
/// Plain record without behaviour: written in batches by the history writer, read for disputes and analytics.
/// </summary>
public sealed class CourierLocationRecord
{
    public long Id { get; set; }

    public Guid CourierId { get; set; }

    public Guid? DeliveryId { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double? SpeedKmh { get; set; }

    public DateTimeOffset RecordedAt { get; set; }
}
