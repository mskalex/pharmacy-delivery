using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Payments;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Write-side access to payments.</summary>
public interface IPaymentRepository
{
    /// <summary>Adds a payment attempt.</summary>
    void Add(Payment payment);

    /// <summary>Payment by the PSP's identifier (tracked), or null.</summary>
    Task<Payment?> FindByProviderIdAsync(string provider, string providerPaymentId, CancellationToken cancellationToken);

    /// <summary>The captured payment of an order (for refunds), or null.</summary>
    Task<Payment?> FindSucceededForOrderAsync(OrderId orderId, CancellationToken cancellationToken);
}
