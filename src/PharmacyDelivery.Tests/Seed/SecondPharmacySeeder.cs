using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Tests.Seed;

/// <summary>Second 24/7 pharmacy (~1 km away) stocking products 1 and 2, with its pharmacist.</summary>
[SeedProfile(TestSeedProfiles.Reassign, Order = 20)]
public sealed class SecondPharmacySeeder(PharmacyDeliveryDbContext db, SeedPasswordHasher passwords, IClock clock) : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        db.Pharmacies.Add(Pharmacy.Create(TestWorld.Pharmacy2, "T-2", "Test Pharmacy 2", "176 9th Ave", "New York", "10011",
            new GeoPoint(40.7453, -74.0018), OpeningHours.NonStop()));
        db.StockItems.Add(StockItem.Create(TestWorld.Pharmacy2, TestWorld.Product1, 50, now));
        db.StockItems.Add(StockItem.Create(TestWorld.Pharmacy2, TestWorld.Product2, 50, now));
        new TestAccounts(db, passwords, now).AddUser(TestWorld.Pharmacist2, "Pharmacist T2", Roles.Pharmacist,
            pharmacy: TestWorld.Pharmacy2);
        await db.SaveChangesAsync(cancellationToken);
    }
}
