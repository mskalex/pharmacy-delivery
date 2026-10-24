using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Places orders: selects the pharmacy, reserves stock under row locks and creates the order.</summary>
public interface ICheckoutService
{
    /// <summary>Creates an order from the customer's cart; a repeated Idempotency-Key returns the existing order.</summary>
    Task<CheckoutResult> CheckoutAsync(Guid customerId, string idempotencyKey, AddressInput address, CancellationToken cancellationToken);
}
