using Microsoft.Extensions.Logging;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Services;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Lock ordering rule used across the system (prevents deadlocks):
/// <b>order row first, then stock rows ordered by product id</b>. The expiry job therefore finds candidate
/// orders without locks and then locks each order with <c>SKIP LOCKED</c> before touching reservations.
/// </summary>
public sealed class StockReservationService(
    IStockRepository stock,
    IStockReservationRepository reservations,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<StockReservationService> logger) : IStockReservationService
{
    public async Task ReserveAsync(
        OrderId orderId,
        PharmacyId pharmacyId,
        IReadOnlyCollection<(ProductId ProductId, int Quantity)> lines,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        using var activity = PharmacyDeliveryTelemetry.StartActivity("stock.reserve");
        activity?.SetTag("pharmacy.id", pharmacyId.Value);
        activity?.SetTag("order.id", orderId.Value);

        var merged = lines
            .GroupBy(l => l.ProductId)
            .Select(g => (ProductId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        var locked = await stock.LockAsync(pharmacyId, merged.Select(l => l.ProductId).ToList(), cancellationToken);
        StockAllocator.ReserveAll(locked, merged, now);

        reservations.AddRange(merged.Select(l =>
            StockReservation.Create(orderId, pharmacyId, l.ProductId, l.Quantity, now, expiresAt)));
    }

    public async Task ReleaseForOrderAsync(OrderId orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await reservations.GetActiveForOrderAsync(orderId, cancellationToken);
        await ApplyAsync(active, (item, reservation) => item.Release(reservation.Quantity, now), r => r.Release(now), cancellationToken);
    }

    public async Task CommitForOrderAsync(OrderId orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var active = await reservations.GetActiveForOrderAsync(orderId, cancellationToken);
        await ApplyAsync(active, (item, reservation) => item.Commit(reservation.Quantity, now), r => r.Commit(now), cancellationToken);
    }

    public async Task ConfirmPaidAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        foreach (var reservation in await reservations.GetActiveForOrderAsync(orderId, cancellationToken))
        {
            reservation.ConfirmPaid();
        }
    }

    public async Task<int> ExpireAsync(int maxOrders, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var candidates = await reservations.FindOrdersWithExpiredReservationsAsync(now, maxOrders, cancellationToken);
        var processed = 0;

        foreach (var orderId in candidates)
        {
            var expired = await unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    // SKIP LOCKED: another instance or a concurrent payment webhook is working on this order.
                    var order = await orders.TryGetForUpdateSkipLockedAsync(orderId, ct);
                    if (order is null)
                        return false;

                    var active = await reservations.GetActiveForOrderAsync(orderId, ct);
                    if (active.Count == 0)
                        return false;

                    if (order.Status is OrderStatus.AwaitingPayment or OrderStatus.PaymentFailed)
                    {
                        await ApplyAsync(active, (item, r) => item.Release(r.Quantity, now), r => r.Release(now), ct);
                        order.Cancel("Reservation expired before payment.", now);
                        await unitOfWork.SaveChangesAsync(ct);
                        return true;
                    }

                    // Paid in the meantime: the webhook won the race, keep the stock.
                    foreach (var reservation in active)
                    {
                        reservation.ConfirmPaid();
                    }

                    await unitOfWork.SaveChangesAsync(ct);
                    return false;
                },
                cancellationToken);

            if (expired)
            {
                processed++;
                PharmacyDeliveryTelemetry.ReservationsExpired.Add(1);
                logger.LogInformation("Reservation of order {OrderId} expired; order cancelled", orderId);
            }
        }

        return processed;
    }

    private async Task ApplyAsync(
        IReadOnlyList<StockReservation> active,
        Action<StockItem, StockReservation> stockAction,
        Action<StockReservation> reservationAction,
        CancellationToken cancellationToken)
    {
        if (active.Count == 0)
            return;

        var now = clock.UtcNow;
        foreach (var group in active.GroupBy(r => r.PharmacyId))
        {
            var locked = (await stock.LockAsync(group.Key, group.Select(r => r.ProductId).Distinct().ToList(), cancellationToken))
                .ToDictionary(s => s.ProductId);

            foreach (var reservation in group)
            {
                if (locked.TryGetValue(reservation.ProductId, out var item))
                {
                    stockAction(item, reservation);
                }
                else
                {
                    logger.LogWarning(
                        "Stock row for product {ProductId} in pharmacy {PharmacyId} is missing at {Now}",
                        reservation.ProductId,
                        reservation.PharmacyId,
                        now);
                }

                reservationAction(reservation);
            }
        }
    }
}
