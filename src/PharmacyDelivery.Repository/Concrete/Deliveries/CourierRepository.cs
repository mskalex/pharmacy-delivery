using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class CourierRepository(PharmacyDeliveryDbContext db) : ICourierRepository
{
    public Task<Courier?> GetAsync(CourierId id, CancellationToken cancellationToken) =>
        db.Couriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
}
