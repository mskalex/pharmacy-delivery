using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Pharmacy panel: queue of orders of the pharmacist's pharmacy.</summary>
[Route(RoutePrefix + "pharmacy/orders")]
[Authorize(Policy = Policies.Pharmacist)]
public sealed class PharmacyOrdersController(IPharmacyOrderService pharmacyOrders) : ApiControllerBase
{
    private Guid PharmacyId =>
        CurrentUser.PharmacyId ?? throw new ForbiddenException(ErrorCodes.Forbidden, "No pharmacy bound to the account.");

    /// <summary>Active orders; <c>acceptDeadline</c> shows when an unaccepted order will be reassigned.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PharmacyQueueItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PharmacyQueueItemResponse>>> Queue(CancellationToken cancellationToken) =>
        (await pharmacyOrders.GetQueueAsync(PharmacyId, cancellationToken)).Select(o => o.ToResponse()).ToList();

    /// <summary>Accepts an assigned order: the pharmacy commits to preparing it and the acceptance timeout stops.</summary>
    [HttpPost("{orderId:guid}/accept")]
    [Audited("pharmacy.accept")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Accept(Guid orderId, CancellationToken cancellationToken)
    {
        await pharmacyOrders.AcceptAsync(PharmacyId, orderId, cancellationToken);
        return NoContent();
    }

    /// <summary>Rejects the order; it is moved to the next pharmacy able to fulfil it (or cancelled and refunded).</summary>
    [HttpPost("{orderId:guid}/reject")]
    [Audited("pharmacy.reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reject(Guid orderId, RejectOrderRequest request, CancellationToken cancellationToken)
    {
        await pharmacyOrders.RejectAsync(PharmacyId, orderId, request.Reason, cancellationToken);
        return NoContent();
    }

    /// <summary>Order packed: stock is committed and a courier is dispatched.</summary>
    [HttpPost("{orderId:guid}/ready")]
    [Audited("pharmacy.ready")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Ready(Guid orderId, CancellationToken cancellationToken)
    {
        await pharmacyOrders.MarkReadyAsync(PharmacyId, orderId, cancellationToken);
        return NoContent();
    }
}
