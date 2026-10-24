using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Tests.Seed;

/// <summary>Two extra couriers: one 200 m from pharmacy 1, one ~3 km away; the base courier reports a stale position.</summary>
[SeedProfile(TestSeedProfiles.Dispatch, Order = 20)]
public sealed class DispatchSeeder(
    PharmacyDeliveryDbContext db,
    SeedPasswordHasher passwords,
    ICourierLocationStore locations,
    IClock clock)
    : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var accounts = new TestAccounts(db, passwords, now);
        await accounts.AddCourierAsync(locations, TestWorld.NearCourier, "Near Courier", 40.7490, -73.9866, cancellationToken);
        await accounts.AddCourierAsync(locations, TestWorld.FarCourier, "Far Courier", 40.7740, -73.9700, cancellationToken);

        // The base courier reports a stale position: dispatch must ignore it.
        await locations.UpdateAsync(
            new CourierPosition(TestWorld.CourierIdOf(TestWorld.Courier).Value, 40.7477, -73.9880, now.AddMinutes(-30)), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
