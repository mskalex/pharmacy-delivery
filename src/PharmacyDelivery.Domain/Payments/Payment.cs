using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Domain.Payments;

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Refunded,
}

/// <summary>
/// Payment attempt at an external PSP. <c>(Provider, ProviderPaymentId)</c> is unique in the database, which makes
/// webhook processing idempotent even under concurrent deliveries of the same notification.
/// </summary>
public sealed class Payment : AggregateRoot<Guid>
{
    private Payment()
    {
    }

    public OrderId OrderId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderPaymentId { get; private set; } = string.Empty;

    public Money Amount { get; private set; } = Money.Zero;

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static Payment Start(OrderId orderId, string provider, string providerPaymentId, Money amount, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            Provider = Guard.NotBlank(provider),
            ProviderPaymentId = Guard.NotBlank(providerPaymentId),
            Amount = amount,
            Status = PaymentStatus.Pending,
            CreatedAt = now,
        };

    /// <summary>Returns false when the payment was already in the target state (duplicate notification).</summary>
    public bool MarkSucceeded(DateTimeOffset now) => Complete(PaymentStatus.Succeeded, now);

    public bool MarkFailed(DateTimeOffset now) => Complete(PaymentStatus.Failed, now);

    public void MarkRefunded(DateTimeOffset now)
    {
        if (Status != PaymentStatus.Succeeded)
            throw new DomainException(ErrorCodes.ValidationFailed, $"Only a succeeded payment can be refunded (status {Status}).");

        Status = PaymentStatus.Refunded;
        CompletedAt = now;
    }

    private bool Complete(PaymentStatus target, DateTimeOffset now)
    {
        if (Status == target)
            return false;

        if (Status != PaymentStatus.Pending)
            throw new DomainException(ErrorCodes.PaymentDuplicate, $"Payment {ProviderPaymentId} is already {Status}.");

        Status = target;
        CompletedAt = now;
        return true;
    }
}
