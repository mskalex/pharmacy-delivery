using PharmacyDelivery.Domain.Catalog;

namespace PharmacyDelivery.Repository.Models;

public sealed record ProductDetails(
    Guid Id,
    string Name,
    string Ean,
    ProductCategory Category,
    string Description,
    decimal Price,
    string Currency,
    bool IsActive);
