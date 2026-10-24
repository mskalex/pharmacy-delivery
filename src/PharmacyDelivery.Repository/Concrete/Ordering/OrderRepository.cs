using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

// Write side: tracked aggregates. Row locks follow the global lock ordering rule:
// order row first, then stock rows ordered by product id (see DESIGN.md §10).

public sealed class OrderRepository(PharmacyDeliveryDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);

    public Task<Order?> GetAsync(OrderId id, CancellationToken cancellationToken) =>
        db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<Order?> GetForUpdateAsync(OrderId id, CancellationToken cancellationToken) =>
        await RowLocks.TryLockAsync(db, "orders", id.Value, skipLocked: false, cancellationToken)
            ? await LoadTrackedAsync(id, cancellationToken)
            : null;

    public async Task<Order?> TryGetForUpdateSkipLockedAsync(OrderId id, CancellationToken cancellationToken) =>
        await RowLocks.TryLockAsync(db, "orders", id.Value, skipLocked: true, cancellationToken)
            ? await LoadTrackedAsync(id, cancellationToken)
            : null;

    public Task<Order?> FindByIdempotencyKeyAsync(UserId customerId, string idempotencyKey, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.CustomerId == customerId && o.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<OrderId>> FindAcceptanceTimeoutsAsync(DateTimeOffset assignedBefore, int limit,
        CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.AssignedToPharmacy && o.AssignedAt < assignedBefore)
            .OrderBy(o => o.AssignedAt)
            .Select(o => o.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrderId>> FindAwaitingDispatchAsync(DateTimeOffset readyBefore, int limit,
        CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.ReadyForPickup && o.UpdatedAt < readyBefore)
            .OrderBy(o => o.UpdatedAt)
            .Select(o => o.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>Loaded after the row lock is held, so the values are the latest committed ones.</summary>
    private Task<Order?> LoadTrackedAsync(OrderId id, CancellationToken cancellationToken) =>
        db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
}
