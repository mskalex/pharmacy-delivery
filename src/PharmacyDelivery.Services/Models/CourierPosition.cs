namespace PharmacyDelivery.Services.Models;

public sealed record CourierPosition(Guid CourierId, double Latitude, double Longitude, DateTimeOffset RecordedAt);
