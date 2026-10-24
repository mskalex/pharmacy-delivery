namespace PharmacyDelivery.Services.Models;

public sealed record PaymentNotification(string ProviderPaymentId, string Status, decimal Amount, string Currency);
