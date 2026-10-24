using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class PharmacyRepository(PharmacyDeliveryDbContext db) : IPharmacyRepository
{
    public Task<Pharmacy?> GetAsync(PharmacyId id, CancellationToken cancellationToken) =>
        db.Pharmacies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Pharmacy>> GetByIdsAsync(IReadOnlyCollection<PharmacyId> ids, CancellationToken cancellationToken) =>
        await db.Pharmacies.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PharmacyId>> GetActiveIdsAsync(CancellationToken cancellationToken) =>
        await db.Pharmacies.AsNoTracking().Where(p => p.IsActive).Select(p => p.Id).ToListAsync(cancellationToken);
}
