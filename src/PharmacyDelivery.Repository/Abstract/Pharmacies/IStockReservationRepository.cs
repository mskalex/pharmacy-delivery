using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Access to stock reservations of orders.</summary>
public interface IStockReservationRepository
{
    /// <summary>Adds reservations created by a checkout or a reassignment.</summary>
    void AddRange(IEnumerable<StockReservation> reservations);

    /// <summary>Active reservations of an order (tracked), ordered by product id.</summary>
    Task<IReadOnlyList<StockReservation>> GetActiveForOrderAsync(OrderId orderId, CancellationToken cancellationToken);

    /// <summary>Candidate orders with expired unpaid reservations (read without locks; the caller locks each order).</summary>
    Task<IReadOnlyList<OrderId>> FindOrdersWithExpiredReservationsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
}
