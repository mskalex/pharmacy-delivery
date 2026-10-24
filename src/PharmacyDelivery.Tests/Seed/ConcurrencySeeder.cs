using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Tests.Seed;

/// <summary>Scarce product (10 units, pharmacy 1 only) and 50 customers for the concurrent checkout test.</summary>
[SeedProfile(TestSeedProfiles.Concurrency, Order = 20)]
public sealed class ConcurrencySeeder(PharmacyDeliveryDbContext db, SeedPasswordHasher passwords, IClock clock) : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        db.Products.Add(Product.Create(TestWorld.ScarceProduct, "Scarce Syrup", "0030000000048", ProductCategory.ForChildren, "test",
            Money.OfUsd(15)));
        db.StockItems.Add(StockItem.Create(TestWorld.Pharmacy1, TestWorld.ScarceProduct, TestWorld.ScarceStock, now));
        var accounts = new TestAccounts(db, passwords, now);
        for (var i = 1; i <= 50; i++)
            accounts.AddUser(TestWorld.ConcurrencyCustomer(i), "Customer " + i, Roles.Customer);

        await db.SaveChangesAsync(cancellationToken);
    }
}
