using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class DeliveryQueries(PharmacyDeliveryDbContext db) : IDeliveryQueries
{
    // Hot path of GPS ingestion (every courier, every few seconds): compiled once, no LINQ translation per call.
    private static readonly Func<PharmacyDeliveryDbContext, CourierId, Task<ActiveDeliveryRow?>> ActiveForCourier =
        EF.CompileAsyncQuery((PharmacyDeliveryDbContext context, CourierId courierId) =>
            context.Deliveries.AsNoTracking()
                .Where(d => d.CourierId == courierId && (d.Status == DeliveryStatus.Assigned || d.Status == DeliveryStatus.InDelivery))
                .Join(
                    context.Orders,
                    d => d.OrderId,
                    o => o.Id,
                    (d, o) => new ActiveDeliveryRow(d.Id, d.OrderId, o.CustomerId, d.Status, o.DeliveryAddress.Latitude,
                        o.DeliveryAddress.Longitude))
                .FirstOrDefault());

    public async Task<CurrentDeliveryInfo?> GetCurrentForCourierAsync(Guid courierId, CancellationToken cancellationToken)
    {
        var id = new CourierId(courierId);
        var delivery = await db.Deliveries.AsNoTracking()
            .Where(d => d.CourierId == id && (d.Status == DeliveryStatus.Assigned || d.Status == DeliveryStatus.InDelivery))
            .FirstOrDefaultAsync(cancellationToken);
        if (delivery is null)
            return null;

        var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == delivery.OrderId, cancellationToken);
        var pharmacy = await db.Pharmacies.AsNoTracking().FirstAsync(p => p.Id == delivery.PharmacyId, cancellationToken);
        return new CurrentDeliveryInfo(
            delivery.Id.Value,
            order.Id.Value,
            order.Number,
            delivery.Status,
            QueryMapping.ToInfo(pharmacy),
            new AddressInput(order.DeliveryAddress.Street, order.DeliveryAddress.City, order.DeliveryAddress.PostalCode,
                order.DeliveryAddress.Latitude, order.DeliveryAddress.Longitude));
    }

    public async Task<ActiveDeliveryRef?> GetActiveRefForCourierAsync(Guid courierId, CancellationToken cancellationToken)
    {
        var row = await ActiveForCourier(db, new CourierId(courierId));
        return row is null
            ? null
            : new ActiveDeliveryRef(row.DeliveryId.Value, row.OrderId.Value, row.CustomerId.Value, row.Status, row.Latitude, row.Longitude);
    }

    public async Task<IReadOnlyList<Guid>> GetAvailableCourierIdsAsync(IReadOnlyCollection<Guid> courierIds,
        CancellationToken cancellationToken)
    {
        var ids = courierIds.Select(id => new CourierId(id)).ToList();
        var available = await db.Couriers.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.Status == CourierStatus.Available)
            .Where(c => !db.Deliveries.Any(d =>
                d.CourierId == c.Id && (d.Status == DeliveryStatus.Assigned || d.Status == DeliveryStatus.InDelivery)))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        return available.Select(id => id.Value).ToList();
    }

    private sealed record ActiveDeliveryRow(
        DeliveryId DeliveryId,
        OrderId OrderId,
        UserId CustomerId,
        DeliveryStatus Status,
        double Latitude,
        double Longitude);
}
