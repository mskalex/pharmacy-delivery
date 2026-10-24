using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Payments through the PSP: start, webhook processing, refunds.</summary>
public interface IPaymentService
{
    /// <summary>Creates a payment at the PSP and returns the redirect URL of its payment page.</summary>
    Task<PaymentStartResult> StartAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>Verifies the HMAC signature and processes a PSP notification idempotently.</summary>
    Task<string> HandleWebhookAsync(string rawBody, long timestamp, string? signature, CancellationToken cancellationToken);

    /// <summary>Refunds the captured payment of a cancelled order (no-op when nothing was captured).</summary>
    Task RefundCancelledOrderAsync(OrderId orderId, CancellationToken cancellationToken);
}
