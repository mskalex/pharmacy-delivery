using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class StockReservationRepository(PharmacyDeliveryDbContext db) : IStockReservationRepository
{
    public void AddRange(IEnumerable<StockReservation> reservations) => db.StockReservations.AddRange(reservations);

    public async Task<IReadOnlyList<StockReservation>> GetActiveForOrderAsync(OrderId orderId, CancellationToken cancellationToken) =>
        await db.StockReservations
            .Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Active)
            .OrderBy(r => r.ProductId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrderId>> FindOrdersWithExpiredReservationsAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken) =>
        await db.StockReservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Active && r.ExpiresAt != null && r.ExpiresAt < now)
            .Select(r => r.OrderId)
            .Distinct()
            .OrderBy(id => id)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
