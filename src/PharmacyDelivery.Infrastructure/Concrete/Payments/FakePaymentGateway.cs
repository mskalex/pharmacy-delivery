using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Fake PSP for Development/Test: creates payments locally and, when the "payment page" is completed, calls the
/// real webhook endpoint over HTTP with an HMAC-signed body — exactly like a real PSP would.
/// </summary>
public sealed class FakePaymentGateway(IOptions<PaymentsOptions> options, ILogger<FakePaymentGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "fake";

    public string Provider => ProviderName;

    public Task<PaymentInitiation> CreatePaymentAsync(Guid orderId, string orderNumber, Money amount, CancellationToken cancellationToken)
    {
        var providerPaymentId = "fake_" + Guid.NewGuid().ToString("N");
        var redirect = $"{options.Value.FakeWebhookBaseUrl.TrimEnd('/')}/api/v1/dev/psp/payments/{providerPaymentId}";
        return Task.FromResult(new PaymentInitiation(providerPaymentId, redirect));
    }

    public Task RefundAsync(string providerPaymentId, Money amount, CancellationToken cancellationToken)
    {
        logger.LogInformation("Fake PSP refund of {Amount} for payment {PaymentId}", amount, providerPaymentId);
        return Task.CompletedTask;
    }
}
