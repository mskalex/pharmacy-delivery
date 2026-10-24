using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Concrete;

internal static class PharmacyMapping
{
    public static PharmacyInfo ToInfo(Pharmacy pharmacy) =>
        new(
            pharmacy.Id.Value,
            pharmacy.Code,
            pharmacy.Name,
            pharmacy.Street,
            pharmacy.City,
            pharmacy.PostalCode,
            pharmacy.Location.Latitude,
            pharmacy.Location.Longitude);

    public static AddressInput ToInput(DeliveryAddress address) =>
        new(address.Street, address.City, address.PostalCode, address.Latitude, address.Longitude);
}
