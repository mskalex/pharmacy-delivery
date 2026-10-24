using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

internal static class QueryMapping
{
    public static PharmacyInfo ToInfo(Pharmacy p) =>
        new(p.Id.Value, p.Code, p.Name, p.Street, p.City, p.PostalCode, p.Location.Latitude, p.Location.Longitude);

    public static OrderItemView ToView(OrderItem i) =>
        new(i.ProductId.Value, i.ProductName, i.UnitPrice.Amount, i.Quantity, i.LineTotal.Amount);
}
