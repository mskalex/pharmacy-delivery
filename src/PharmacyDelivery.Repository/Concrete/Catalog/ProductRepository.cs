using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class ProductRepository(PharmacyDeliveryDbContext db) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IReadOnlyCollection<ProductId> ids, CancellationToken cancellationToken) =>
        await db.Products.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<Product?> GetAsync(ProductId id, CancellationToken cancellationToken) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
}
