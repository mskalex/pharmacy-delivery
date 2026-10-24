namespace PharmacyDelivery.Services.Models;

public sealed record NearbyCourier(Guid CourierId, double DistanceKm, DateTimeOffset RecordedAt);
