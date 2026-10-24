using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Product catalog (OTC products). Cached in HybridCache (memory + Redis).</summary>
[Route(RoutePrefix + "catalog/products")]
[AllowAnonymous]
public sealed class CatalogController(ICatalogService catalog) : ApiControllerBase
{
    /// <summary>Searches active products by name (trigram index) and category.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProductSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ProductSummaryResponse>>> Search(
        [FromQuery] string? search,
        [FromQuery] ProductCategory? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (search is { Length: > 100 })
            throw ValidationFailedException.ForField("search", ErrorCodes.ValidationFailed, "Search text is too long.");

        var result = await catalog.SearchAsync(search, category, new PageRequest(page, pageSize), cancellationToken);
        return result.ToResponse(p => p.ToResponse());
    }

    /// <summary>Product details (served from the catalog cache); 404 for an unknown product.</summary>
    [HttpGet("{productId:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Get(Guid productId, CancellationToken cancellationToken) =>
        (await catalog.GetAsync(productId, cancellationToken)).ToResponse();
}
