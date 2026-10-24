using PharmacyDelivery.Domain.Catalog;

namespace PharmacyDelivery.Repository.Models;

// Read models returned by query objects (AsNoTracking + projections). They are neither domain entities nor API DTOs:
// the API maps them to versioned DTOs (Contracts/V1).

public sealed record ProductSummary(Guid Id, string Name, ProductCategory Category, decimal Price, string Currency);
