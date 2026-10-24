using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Customer-facing order use cases.</summary>
public interface IOrderService
{
    /// <summary>Orders of the customer, newest first.</summary>
    Task<PagedResult<OrderSummary>> GetMyOrdersAsync(Guid customerId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Order details visible to its customer, pharmacy, courier or an admin (404 otherwise).</summary>
    Task<OrderDetails> GetDetailsAsync(Guid orderId, ICurrentUser user, CancellationToken cancellationToken);

    /// <summary>Cancels the order while allowed; releases the stock and refunds a captured payment.</summary>
    Task<OrderDetails> CancelAsync(Guid orderId, Guid customerId, string reason, ICurrentUser user, CancellationToken cancellationToken);
}
