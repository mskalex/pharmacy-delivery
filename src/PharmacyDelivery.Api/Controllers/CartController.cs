using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Shopping cart of the signed-in customer (stored in Redis with a TTL).</summary>
[Route(RoutePrefix + "cart")]
[Authorize(Policy = Policies.Customer)]
public sealed class CartController(ICartService cart) : ApiControllerBase
{
    /// <summary>Current cart: every line is priced from the catalog and flagged when the product is no longer available.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartResponse>> Get(CancellationToken cancellationToken) =>
        (await cart.GetAsync(CurrentUser.UserId, cancellationToken)).ToResponse();

    /// <summary>Adds a product or sets its quantity.</summary>
    [HttpPut("items/{productId:guid}")]
    [AuditExempt("The cart lives in Redis; no database change to audit.")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CartResponse>>
        SetItem(Guid productId, SetCartItemRequest request, CancellationToken cancellationToken) =>
        (await cart.SetItemAsync(CurrentUser.UserId, productId, request.Quantity, cancellationToken)).ToResponse();

    /// <summary>Removes a product from the cart and returns the updated cart.</summary>
    [HttpDelete("items/{productId:guid}")]
    [AuditExempt("The cart lives in Redis; no database change to audit.")]
    [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CartResponse>> RemoveItem(Guid productId, CancellationToken cancellationToken) =>
        (await cart.RemoveItemAsync(CurrentUser.UserId, productId, cancellationToken)).ToResponse();

    /// <summary>Empties the cart.</summary>
    [HttpDelete]
    [AuditExempt("The cart lives in Redis; no database change to audit.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        await cart.ClearAsync(CurrentUser.UserId, cancellationToken);
        return NoContent();
    }
}
