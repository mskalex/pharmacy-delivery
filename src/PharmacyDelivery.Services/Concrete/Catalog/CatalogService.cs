using Microsoft.Extensions.Caching.Hybrid;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Catalog reads go through HybridCache (L1 in-process memory + L2 Redis). Entries are tagged with
/// <see cref="CacheTag"/>; any catalog change invalidates the tag, so readers never see a stale price for long.
/// </summary>
public sealed class CatalogService(
    ICatalogQueries queries,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    HybridCache cache) : ICatalogService
{
    public const string CacheTag = "catalog";

    private static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(10),
        LocalCacheExpiration = TimeSpan.FromMinutes(1),
    };

    public async Task<PagedResult<ProductSummary>> SearchAsync(
        string? search,
        ProductCategory? category,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var normalized = string.IsNullOrWhiteSpace(search) ? string.Empty : search.Trim().ToLowerInvariant();
        var key = $"catalog:search:{normalized}:{category}:{page.Page}:{page.PageSize}";
        return await cache.GetOrCreateAsync(
            key,
            (queries, normalized, category, page),
            static (state, ct) => new ValueTask<PagedResult<ProductSummary>>(
                state.queries.SearchAsync(state.normalized, state.category, state.page, ct)),
            EntryOptions,
            [CacheTag],
            cancellationToken);
    }

    public async Task<ProductDetails> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await cache.GetOrCreateAsync(
            $"catalog:product:{productId}",
            (queries, productId),
            static (state, ct) => new ValueTask<ProductDetails?>(state.queries.GetAsync(state.productId, ct)),
            EntryOptions,
            [CacheTag],
            cancellationToken);

        return product ?? throw new NotFoundException(ErrorCodes.ProductNotFound, $"Product {productId} was not found.");
    }

    public async Task ChangePriceAsync(Guid productId, decimal newPrice, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(new ProductId(productId), cancellationToken)
                      ?? throw new NotFoundException(ErrorCodes.ProductNotFound, $"Product {productId} was not found.");

        product.ChangePrice(Money.OfUsd(newPrice));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveByTagAsync(CacheTag, cancellationToken);
    }
}
