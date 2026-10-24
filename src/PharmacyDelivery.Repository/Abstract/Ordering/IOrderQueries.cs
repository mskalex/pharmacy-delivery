using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Read side of orders (AsNoTracking + projections).</summary>
public interface IOrderQueries
{
    /// <summary>Orders of a customer, newest first.</summary>
    Task<PagedResult<OrderSummary>> GetCustomerOrdersAsync(Guid customerId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Order with items, status history, fulfilling pharmacy and courier, or null.</summary>
    Task<OrderDetails?> GetDetailsAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Owner, pharmacy, status and courier of an order — input of resource-based authorization.</summary>
    Task<OrderAccessInfo?> GetAccessInfoAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Active orders of a pharmacy with the deadline for accepting them.</summary>
    Task<IReadOnlyList<PharmacyOrderSummary>> GetPharmacyQueueAsync(Guid pharmacyId, TimeSpan acceptanceTimeout,
        CancellationToken cancellationToken);
}
