using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Infrastructure.Concrete;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>PSP notifications. Authenticated with an HMAC signature (not JWT) and processed idempotently.</summary>
[Route(RoutePrefix + "webhooks/psp")]
[AllowAnonymous]
public sealed class PaymentWebhooksController(IPaymentService payments) : ApiControllerBase
{
    /// <summary>Payment status notification. Headers: X-Psp-Timestamp (unix seconds), X-Psp-Signature (hex HMAC-SHA256 of "timestamp.body").</summary>
    [HttpPost]
    [Audited("payment.webhook")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Receive(
        [FromHeader(Name = FakePspSimulator.TimestampHeader)]
        long timestamp,
        [FromHeader(Name = FakePspSimulator.SignatureHeader)]
        string? signature,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var outcome = await payments.HandleWebhookAsync(body, timestamp, signature, cancellationToken);
        return Ok(new { outcome });
    }
}
