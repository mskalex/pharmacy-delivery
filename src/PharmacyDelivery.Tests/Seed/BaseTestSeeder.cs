using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Tests.Seed;

/// <summary>Base world of every test template: admin, pharmacy 1 (24/7), three products, customers, a pharmacist, one courier.</summary>
[SeedProfile(TestSeedProfiles.Minimal, Order = 10)]
[SeedProfile(TestSeedProfiles.Concurrency, Order = 10)]
[SeedProfile(TestSeedProfiles.Reassign, Order = 10)]
[SeedProfile(TestSeedProfiles.Dispatch, Order = 10)]
public sealed class BaseTestSeeder(
    PharmacyDeliveryDbContext db,
    SeedPasswordHasher passwords,
    ICourierLocationStore locations,
    IClock clock)
    : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        db.Pharmacies.Add(Pharmacy.Create(TestWorld.Pharmacy1, "T-1", "Test Pharmacy 1", "1250 Broadway", "New York", "10001",
            new GeoPoint(40.7476, -73.9881), OpeningHours.NonStop()));
        db.Products.Add(Product.Create(TestWorld.Product1, "Test Acetaminophen", "0030000000017", ProductCategory.PainRelief, "test",
            Money.OfUsd(10)));
        db.Products.Add(Product.Create(TestWorld.Product2, "Test Ibuprofen", "0030000000024", ProductCategory.PainRelief, "test",
            Money.OfUsd(20)));
        db.Products.Add(Product.Create(TestWorld.Product3, "Test Vitamin", "0030000000031", ProductCategory.VitaminsAndSupplements,
            "test", Money.OfUsd(30)));
        foreach (var product in new[] { TestWorld.Product1, TestWorld.Product2, TestWorld.Product3 })
            db.StockItems.Add(StockItem.Create(TestWorld.Pharmacy1, product, 50, now));

        var accounts = new TestAccounts(db, passwords, now);
        accounts.AddUser(TestWorld.Admin, "Administrator", Roles.Admin);
        accounts.AddUser(TestWorld.Customer, "Test Customer", Roles.Customer);
        accounts.AddUser(TestWorld.OtherCustomer, "Other Customer", Roles.Customer);
        accounts.AddUser(TestWorld.Pharmacist1, "Pharmacist T1", Roles.Pharmacist, pharmacy: TestWorld.Pharmacy1);
        await accounts.AddCourierAsync(locations, TestWorld.Courier, "Courier T1", 40.7479, -73.9878, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
