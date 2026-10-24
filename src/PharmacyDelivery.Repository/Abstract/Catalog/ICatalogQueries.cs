using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Read side of the catalog (AsNoTracking + projections).</summary>
public interface ICatalogQueries
{
    /// <summary>Active products matching the name fragment (trigram index) and category, sorted by name.</summary>
    Task<PagedResult<ProductSummary>> SearchAsync(string? search, ProductCategory? category, PageRequest page,
        CancellationToken cancellationToken);

    /// <summary>One product (also inactive ones), or null.</summary>
    Task<ProductDetails?> GetAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Products with the given ids (missing ids are skipped).</summary>
    Task<IReadOnlyList<ProductDetails>> GetManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}
