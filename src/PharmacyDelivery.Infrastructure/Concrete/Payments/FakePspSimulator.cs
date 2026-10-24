using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Simulates the PSP side of a payment: signs and sends the webhook notification.</summary>
public sealed class FakePspSimulator(
    HttpClient http,
    IPaymentRepository payments,
    IClock clock,
    IOptions<PaymentsOptions> options)
{
    public const string TimestampHeader = "X-Psp-Timestamp";
    public const string SignatureHeader = "X-Psp-Signature";

    public async Task<int> CompleteAsync(string providerPaymentId, bool succeeded, CancellationToken cancellationToken)
    {
        var payment = await payments.FindByProviderIdAsync(FakePaymentGateway.ProviderName, providerPaymentId, cancellationToken)
                      ?? throw new NotFoundException(ErrorCodes.PaymentNotFound, "Unknown payment.");

        var body = JsonSerializer.Serialize(
            new PaymentNotification(providerPaymentId, succeeded ? "succeeded" : "failed", payment.Amount.Amount, payment.Amount.Currency),
            PharmacyDeliveryJsonOptions.Tolerant);
        var timestamp = clock.UtcNow.ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/psp")
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        request.Headers.Add(TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(SignatureHeader, HmacSignature.Compute(options.Value.WebhookSecret, timestamp, body));

        using var response = await http.SendAsync(request, cancellationToken);
        return (int)response.StatusCode;
    }
}
