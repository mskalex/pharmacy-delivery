using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Adapter of the payment service provider (PSP).</summary>
public interface IPaymentGateway
{
    /// <summary>Provider name stored with payments (part of the idempotency key of webhooks).</summary>
    string Provider { get; }

    /// <summary>Creates a payment at the PSP; returns its identifier and the payment page URL.</summary>
    Task<PaymentInitiation> CreatePaymentAsync(Guid orderId, string orderNumber, Money amount, CancellationToken cancellationToken);

    /// <summary>Refunds a captured payment.</summary>
    Task RefundAsync(string providerPaymentId, Money amount, CancellationToken cancellationToken);
}
