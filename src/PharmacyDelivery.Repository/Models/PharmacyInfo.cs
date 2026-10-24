namespace PharmacyDelivery.Repository.Models;

public sealed record PharmacyInfo(
    Guid Id,
    string Code,
    string Name,
    string Street,
    string City,
    string PostalCode,
    double Latitude,
    double Longitude);
