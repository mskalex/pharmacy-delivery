namespace PharmacyDelivery.Infrastructure.Seed;

internal sealed record PersonJson(
    string Email,
    string DisplayName,
    double Latitude = 0,
    double Longitude = 0,
    IReadOnlyList<AddressJson>? Addresses = null);
