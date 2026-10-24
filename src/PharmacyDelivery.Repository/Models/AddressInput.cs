namespace PharmacyDelivery.Repository.Models;

public sealed record AddressInput(string Street, string City, string PostalCode, double Latitude, double Longitude);
