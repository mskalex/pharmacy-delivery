using PharmacyDelivery.Domain.Catalog;

namespace PharmacyDelivery.Infrastructure.Seed;

internal sealed record ProductJson(string Name, ProductCategory Category, decimal Price, string Description);
