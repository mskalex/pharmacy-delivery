using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Infrastructure.Concrete;

[SeedProfile("demo", Order = 20)]
public sealed class PharmaciesSeeder(PharmacyDeliveryDbContext db, SeedDataSource data) : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        foreach (var p in data.Read<List<PharmacyJson>>("pharmacies.json"))
        {
            db.Pharmacies.Add(Pharmacy.Create(
                SeedKeys.Pharmacy(p.Code),
                p.Code,
                p.Name,
                p.Street,
                p.City,
                p.PostalCode,
                new GeoPoint(p.Latitude, p.Longitude),
                p.ToOpeningHours()));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
