using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Courier app: current delivery, pickup/delivery confirmation and GPS ingestion.</summary>
[Route(RoutePrefix + "courier")]
[Authorize(Policy = Policies.Courier)]
public sealed class CourierController(ICourierDeliveryService deliveries, ITrackingService tracking) : ApiControllerBase
{
    private Guid CourierId =>
        CurrentUser.CourierId ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No courier bound to the account.");

    /// <summary>The courier's current delivery with pickup and drop-off points; 204 when the courier is idle.</summary>
    [HttpGet("deliveries/current")]
    [ProducesResponseType(typeof(CurrentDeliveryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<CurrentDeliveryResponse>> Current(CancellationToken cancellationToken)
    {
        var current = await deliveries.GetCurrentAsync(CourierId, cancellationToken);
        return current is null ? NoContent() : current.ToResponse();
    }

    /// <summary>Confirms pickup at the pharmacy: the order goes InDelivery and the customer starts seeing the courier live.</summary>
    [HttpPost("deliveries/{deliveryId:guid}/pickup")]
    [Audited("delivery.pickup")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> PickUp(Guid deliveryId, CancellationToken cancellationToken)
    {
        await deliveries.PickUpAsync(CourierId, deliveryId, cancellationToken);
        return NoContent();
    }

    /// <summary>Confirms hand-over to the customer: the order is Delivered and the courier becomes available again.</summary>
    [HttpPost("deliveries/{deliveryId:guid}/deliver")]
    [Audited("delivery.complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deliver(Guid deliveryId, CancellationToken cancellationToken)
    {
        await deliveries.DeliverAsync(CourierId, deliveryId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Batch of GPS points (sent every few seconds; buffered by the app when offline). Outliers, stale and
    /// out-of-order points are dropped; the latest position is pushed to the customer while the order is InDelivery.
    /// </summary>
    [HttpPost("locations")]
    [EnableRateLimiting(RateLimits.Tracking)]
    [AuditExempt("High-frequency GPS ingestion: positions go to Redis and the partitioned history table.")]
    [ProducesResponseType(typeof(LocationBatchResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<LocationBatchResponse>> Locations(LocationBatchRequest request, CancellationToken cancellationToken)
    {
        var result = await tracking.IngestAsync(CourierId, request.Points.Select(p => p.ToInput()).ToList(), cancellationToken);
        return Accepted(new LocationBatchResponse(result.Accepted, result.Dropped));
    }
}
