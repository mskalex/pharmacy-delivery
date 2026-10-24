using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Api.Configuration;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Administration: catalog prices, blocking users, audit log, data reset (maintenance).</summary>
[Route(RoutePrefix + "admin")]
[Authorize(Policy = Policies.Admin)]
public sealed class AdminController(
    ICatalogService catalog,
    IAuthService auth,
    IAuditService audit,
    IDataMaintenance maintenance,
    IOptions<AdminOptions> options) : ApiControllerBase
{
    /// <summary>Changes a product price; the catalog cache is invalidated by tag.</summary>
    [HttpPut("products/{productId:guid}/price")]
    [Audited("catalog.change_price")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePrice(Guid productId, ChangePriceRequest request, CancellationToken cancellationToken)
    {
        await catalog.ChangePriceAsync(productId, request.Price, cancellationToken);
        return NoContent();
    }

    /// <summary>Blocks a user; all sessions and access tokens are revoked immediately.</summary>
    [HttpPost("users/{userId:guid}/block")]
    [Audited("user.block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> BlockUser(Guid userId, CancellationToken cancellationToken)
    {
        await auth.BlockUserAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Audit log: who changed what (old → new values), filtered by entity or user.</summary>
    [HttpGet("audit")]
    [ProducesResponseType(typeof(PagedResponse<AuditEntryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AuditEntryResponse>>> Audit(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] Guid? userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        (await audit.SearchAsync(entityType, entityId, userId, new PageRequest(page, pageSize), cancellationToken)).ToResponse(e =>
            e.ToResponse());

    /// <summary>
    /// Deletes ALL data and loads a seed template again (Admin:MaintenanceEndpointsEnabled and Seed:AllowReset must
    /// both be true — Development/Test only).
    /// </summary>
    [HttpPost("maintenance/seed/reset")]
    [AuditExempt("A reset truncates every table, including the audit log itself; it is logged instead.")]
    [ProducesResponseType(typeof(SeedResetResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SeedResetResponse>> ResetSeed(SeedResetRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.MaintenanceEndpointsEnabled)
            throw new ForbiddenException(ErrorCodes.MaintenanceDisabled, "Maintenance endpoints are disabled in this environment.");

        try
        {
            await maintenance.ResetAsync(request.Profile, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new ForbiddenException(ErrorCodes.MaintenanceDisabled, ex.Message);
        }

        return new SeedResetResponse(request.Profile ?? "(default)", maintenance.AvailableProfiles);
    }
}
