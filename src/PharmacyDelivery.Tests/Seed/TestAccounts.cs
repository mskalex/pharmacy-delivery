using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Tests.Seed;

/// <summary>Creates test accounts with <see cref="TestWorld.Password"/> and ids derived from the e-mail.</summary>
internal sealed class TestAccounts(PharmacyDeliveryDbContext db, SeedPasswordHasher passwords, DateTimeOffset now)
{
    public void AddUser(string email, string name, string role, PharmacyId? pharmacy = null, CourierId? courier = null) =>
        db.Users.Add(User.Create(TestWorld.UserIdOf(email), email, name, role, passwords.Hash(TestWorld.Password, email, name), now,
            pharmacy, courier));

    /// <summary>Courier entity + its user account + a fresh position in the Redis GEO index.</summary>
    public async Task AddCourierAsync(ICourierLocationStore locations, string email, string name, double lat, double lng,
        CancellationToken cancellationToken)
    {
        var courierId = TestWorld.CourierIdOf(email);
        db.Couriers.Add(Courier.Create(courierId, TestWorld.UserIdOf(email), name));
        AddUser(email, name, Roles.Courier, courier: courierId);
        await locations.UpdateAsync(new CourierPosition(courierId.Value, lat, lng, now), cancellationToken);
    }
}
