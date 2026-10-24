using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Checkout: selects the pharmacy, reserves stock and creates the order.</summary>
[Route(RoutePrefix + "checkout")]
[Authorize(Policy = Policies.Customer)]
public sealed partial class CheckoutController(ICheckoutService checkout) : ApiControllerBase
{
    /// <summary>
    /// Places an order from the cart. Requires the <c>Idempotency-Key</c> header: repeating the request with the same
    /// key returns the same order instead of creating a second one.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimits.Checkout)]
    [Audited("order.checkout")]
    [ProducesResponseType(typeof(CheckoutResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CheckoutResponse>> Checkout(
        CheckoutRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !IdempotencyKeyFormat().IsMatch(idempotencyKey))
        {
            throw ValidationFailedException.ForField("Idempotency-Key", ErrorCodes.ValidationFailed,
                "Header Idempotency-Key (8–64 characters: letters, digits, '-', '_') is required.");
        }

        var result = await checkout.CheckoutAsync(CurrentUser.UserId, idempotencyKey, request.Address.ToInput(), cancellationToken);
        var response = result.ToResponse();
        return result.IsReplay
            ? Ok(response)
            : CreatedAtAction(nameof(OrdersController.Get), "Orders", new { version = "1", orderId = result.OrderId }, response);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex IdempotencyKeyFormat();
}
