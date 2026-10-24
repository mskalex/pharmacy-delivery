namespace PharmacyDelivery.Services.Models;

public sealed record PaymentInitiation(string ProviderPaymentId, string RedirectUrl);
