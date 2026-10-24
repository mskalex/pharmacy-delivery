using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Write-side access to products.</summary>
public interface IProductRepository
{
    /// <summary>Loads several products (tracked).</summary>
    Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<ProductId> ids, CancellationToken cancellationToken);

    /// <summary>Loads a product (tracked), or null.</summary>
    Task<Product?> GetAsync(ProductId id, CancellationToken cancellationToken);
}
