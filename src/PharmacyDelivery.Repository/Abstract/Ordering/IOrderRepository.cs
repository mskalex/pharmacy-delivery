using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Write-side access to orders (tracked aggregates, row locks).</summary>
public interface IOrderRepository
{
    /// <summary>Adds a new order with its items and first history entry.</summary>
    void Add(Order order);

    /// <summary>Loads an order with its items (tracked, without a row lock).</summary>
    Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken);

    /// <summary>Locks the order row (<c>FOR UPDATE</c>) and loads the aggregate. Must run inside a transaction.</summary>
    Task<Order?> GetForUpdateAsync(OrderId id, CancellationToken cancellationToken);

    /// <summary>Like <see cref="GetForUpdateAsync"/> but returns null when another transaction holds the lock (<c>SKIP LOCKED</c>).</summary>
    Task<Order?> TryGetForUpdateSkipLockedAsync(OrderId id, CancellationToken cancellationToken);

    /// <summary>Order already created by the customer with this Idempotency-Key, or null.</summary>
    Task<Order?> FindByIdempotencyKeyAsync(UserId customerId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Orders waiting for pharmacy acceptance longer than allowed.</summary>
    Task<IReadOnlyList<OrderId>> FindAcceptanceTimeoutsAsync(DateTimeOffset assignedBefore, int limit, CancellationToken cancellationToken);

    /// <summary>Orders ready for pickup without an active delivery (dispatch retry).</summary>
    Task<IReadOnlyList<OrderId>> FindAwaitingDispatchAsync(DateTimeOffset readyBefore, int limit, CancellationToken cancellationToken);
}
