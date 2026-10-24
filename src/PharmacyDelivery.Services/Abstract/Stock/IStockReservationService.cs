using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Stock reservations: reserve, release, commit and expiry (lock order: order row, then stock rows by product id).</summary>
public interface IStockReservationService
{
    /// <summary>Locks stock rows and reserves every line (all or nothing). Must run inside a transaction.</summary>
    Task ReserveAsync(
        OrderId orderId,
        PharmacyId pharmacyId,
        IReadOnlyCollection<(ProductId ProductId, int Quantity)> lines,
        DateTimeOffset now,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken);

    /// <summary>Releases active reservations of the order. Must run inside a transaction holding the order lock.</summary>
    Task ReleaseForOrderAsync(OrderId orderId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Goods left the shelf: reservations become committed and on-hand stock decreases.</summary>
    Task CommitForOrderAsync(OrderId orderId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The order was paid: reservations no longer expire.</summary>
    Task ConfirmPaidAsync(OrderId orderId, CancellationToken cancellationToken);

    /// <summary>Releases expired reservations of unpaid orders and cancels those orders. Returns the number of orders processed.</summary>
    Task<int> ExpireAsync(int maxOrders, CancellationToken cancellationToken);
}
