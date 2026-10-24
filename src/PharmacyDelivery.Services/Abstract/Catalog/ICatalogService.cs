using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Catalog use cases with HybridCache (memory + Redis) and tag invalidation.</summary>
public interface ICatalogService
{
    /// <summary>Cached search of active products.</summary>
    Task<PagedResult<ProductSummary>> SearchAsync(string? search, ProductCategory? category, PageRequest page,
        CancellationToken cancellationToken);

    /// <summary>Cached product details; throws NotFound for an unknown id.</summary>
    Task<ProductDetails> GetAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Changes the price and invalidates the catalog cache.</summary>
    Task ChangePriceAsync(Guid productId, decimal newPrice, CancellationToken cancellationToken);
}
