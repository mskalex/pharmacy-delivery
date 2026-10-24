namespace PharmacyDelivery.Services.Models;

public sealed record PaymentStartResult(Guid PaymentId, string ProviderPaymentId, string RedirectUrl, decimal Amount, string Currency);
