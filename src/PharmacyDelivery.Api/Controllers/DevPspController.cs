using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Infrastructure.Concrete;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>
/// Fake PSP payment page (Payments:Provider = Fake only — Development/Test). Completing the payment makes the fake
/// PSP call the real webhook endpoint with a signed notification.
/// </summary>
[Route(RoutePrefix + "dev/psp/payments")]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "v1")]
public sealed class DevPspController(FakePspSimulator simulator, IOptions<PaymentsOptions> options) : ApiControllerBase
{
    /// <summary>Minimal payment page with "pay" and "decline" buttons.</summary>
    [HttpGet("{providerPaymentId}")]
    [Produces("text/html")]
    public IActionResult Page(string providerPaymentId)
    {
        if (options.Value.Provider != "Fake")
            return NotFound();

        var id = WebUtility.HtmlEncode(providerPaymentId);
        var html = string.Create(CultureInfo.InvariantCulture, $$"""
                                                                 <!doctype html><html lang="en"><head><meta charset="utf-8"><title>Fake PSP</title></head>
                                                                 <body style="font-family:sans-serif;max-width:420px;margin:40px auto">
                                                                 <h2>Test payment page</h2><p>Payment <code>{{id}}</code></p>
                                                                 <form method="post" action="{{id}}/complete?result=succeeded"><button>Pay</button></form>
                                                                 <form method="post" action="{{id}}/complete?result=failed"><button>Decline</button></form>
                                                                 </body></html>
                                                                 """);
        return Content(html, "text/html");
    }

    /// <summary>Simulates the PSP decision and sends the signed webhook.</summary>
    [HttpPost("{providerPaymentId}/complete")]
    [AuditExempt("Simulates the external PSP; the resulting webhook call is audited.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Complete(string providerPaymentId, [FromQuery] string result, CancellationToken cancellationToken)
    {
        if (options.Value.Provider != "Fake")
            return NotFound();

        var status = await simulator.CompleteAsync(providerPaymentId, result == "succeeded", cancellationToken);
        return Ok(new { webhookStatus = status });
    }
}
