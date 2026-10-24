using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class DeliveryRepository(PharmacyDeliveryDbContext db) : IDeliveryRepository
{
    public void Add(Delivery delivery) => db.Deliveries.Add(delivery);

    public Task<Delivery?> GetAsync(DeliveryId id, CancellationToken cancellationToken) =>
        db.Deliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<Delivery?> GetActiveForCourierAsync(CourierId courierId, CancellationToken cancellationToken) =>
        db.Deliveries.FirstOrDefaultAsync(
            d => d.CourierId == courierId && (d.Status == DeliveryStatus.Assigned || d.Status == DeliveryStatus.InDelivery),
            cancellationToken);

    public Task<Delivery?> GetActiveForOrderAsync(OrderId orderId, CancellationToken cancellationToken) =>
        db.Deliveries.FirstOrDefaultAsync(
            d => d.OrderId == orderId && (d.Status == DeliveryStatus.Assigned || d.Status == DeliveryStatus.InDelivery),
            cancellationToken);
}
