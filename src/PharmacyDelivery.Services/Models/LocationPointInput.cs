namespace PharmacyDelivery.Services.Models;

public sealed record LocationPointInput(double Latitude, double Longitude, DateTimeOffset RecordedAt, double? SpeedKmh);
