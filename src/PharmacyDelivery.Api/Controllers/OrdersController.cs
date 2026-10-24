using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Orders: list, details (incl. the fulfilling pharmacy), payment, cancellation and tracking snapshot.</summary>
[Route(RoutePrefix + "orders")]
public sealed class OrdersController(IOrderService orders, IPaymentService payments, ITrackingService tracking) : ApiControllerBase
{
    /// <summary>Orders of the signed-in customer, newest first.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.Customer)]
    [ProducesResponseType(typeof(PagedResponse<OrderSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrderSummaryResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        (await orders.GetMyOrdersAsync(CurrentUser.UserId, new PageRequest(page, pageSize), cancellationToken)).ToResponse(o =>
            o.ToResponse());

    /// <summary>Order details. Visible to its customer, the fulfilling pharmacy, the assigned courier and admins (404 otherwise).</summary>
    [HttpGet("{orderId:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Get(Guid orderId, CancellationToken cancellationToken) =>
        (await orders.GetDetailsAsync(orderId, CurrentUser, cancellationToken)).ToResponse();

    /// <summary>Starts the payment; the client is redirected to <c>redirectUrl</c> (PSP payment page).</summary>
    [HttpPost("{orderId:guid}/payments")]
    [Authorize(Policy = Policies.Customer)]
    [Audited("payment.start")]
    [ProducesResponseType(typeof(PaymentStartResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentStartResponse>> StartPayment(Guid orderId, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, (await payments.StartAsync(orderId, CurrentUser.UserId, cancellationToken)).ToResponse());

    /// <summary>Cancels the order (allowed until the pharmacy accepts it); a captured payment is refunded.</summary>
    [HttpPost("{orderId:guid}/cancel")]
    [Authorize(Policy = Policies.Customer)]
    [Audited("order.cancel")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrderResponse>> Cancel(Guid orderId, CancelOrderRequest request, CancellationToken cancellationToken) =>
        (await orders.CancelAsync(orderId, CurrentUser.UserId, request.Reason, CurrentUser, cancellationToken)).ToResponse();

    /// <summary>
    /// Tracking snapshot (fallback for clients without WebSockets: poll every few seconds). The courier position is
    /// present only while the order is InDelivery.
    /// </summary>
    [HttpGet("{orderId:guid}/tracking")]
    [ProducesResponseType(typeof(TrackingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TrackingResponse>> Tracking(Guid orderId, CancellationToken cancellationToken) =>
        (await tracking.GetSnapshotAsync(orderId, CurrentUser, cancellationToken)).ToResponse();
}
