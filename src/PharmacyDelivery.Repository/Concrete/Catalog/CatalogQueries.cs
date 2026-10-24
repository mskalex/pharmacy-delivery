using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

// Read side: AsNoTracking + projections, so entities never enter the change tracker
// (verified by an integration test: ChangeTracker.Entries() is empty after every read).

public sealed class CatalogQueries(PharmacyDeliveryDbContext db) : ICatalogQueries
{
    public async Task<PagedResult<ProductSummary>> SearchAsync(
        string? search,
        ProductCategory? category,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILIKE '%term%' is served by the pg_trgm GIN index (ix_products_name_trgm).
            var pattern = "%" + EscapeLike(search.Trim()) + "%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern, "\\"));
        }

        if (category is not null)
            query = query.Where(p => p.Category == category);

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(p => new ProductSummary(p.Id.Value, p.Name, p.Category, p.Price.Amount, p.Price.Currency))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductSummary>(items, page.Page, page.PageSize, total);
    }

    public Task<ProductDetails?> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        var id = new ProductId(productId);
        return db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductDetails(p.Id.Value, p.Name, p.Ean, p.Category, p.Description, p.Price.Amount, p.Price.Currency,
                p.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductDetails>> GetManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Select(id => new ProductId(id)).ToList();
        return await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new ProductDetails(p.Id.Value, p.Name, p.Ean, p.Category, p.Description, p.Price.Amount, p.Price.Currency,
                p.IsActive))
            .ToListAsync(cancellationToken);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
