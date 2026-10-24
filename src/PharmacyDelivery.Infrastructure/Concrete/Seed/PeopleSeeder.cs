using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Customers with saved addresses, one pharmacist per pharmacy, couriers with start positions (Redis).</summary>
[SeedProfile("demo", Order = 50)]
public sealed class PeopleSeeder(
    PharmacyDeliveryDbContext db,
    SeedDataSource data,
    SeedPasswordHasher passwords,
    ICourierLocationStore locations,
    IClock clock,
    IOptions<SeedOptions> options) : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var password = SeedKeys.FirstNonEmpty(options.Value.UsersPassword)
                       ?? throw new InvalidOperationException("Seed:UsersPassword must be configured for the demo profile.");
        var users = data.Read<UsersJson>("users.json");
        var now = clock.UtcNow;

        foreach (var customer in users.Customers)
        {
            var user = User.Create(
                SeedKeys.User(customer.Email),
                customer.Email,
                customer.DisplayName,
                Roles.Customer,
                passwords.Hash(password, customer.Email, customer.DisplayName),
                now);
            foreach (var a in customer.Addresses ?? [])
                user.AddAddress(new SavedAddress(a.Label, a.Street, a.City, a.PostalCode, a.Latitude, a.Longitude));

            db.Users.Add(user);
        }

        foreach (var pharmacy in data.Read<List<PharmacyJson>>("pharmacies.json"))
        {
            var email = $"pharmacist.{pharmacy.Code.ToLowerInvariant()}@pharmacy-delivery.local";
            var name = "Pharmacist " + pharmacy.Code;
            db.Users.Add(User.Create(
                SeedKeys.User(email),
                email,
                name,
                Roles.Pharmacist,
                passwords.Hash(password, email, name),
                now,
                pharmacyId: SeedKeys.Pharmacy(pharmacy.Code)));
        }

        foreach (var c in users.Couriers)
        {
            var courierId = SeedKeys.Courier(c.Email);
            var userId = SeedKeys.User(c.Email);
            db.Couriers.Add(Courier.Create(courierId, userId, c.DisplayName));
            db.Users.Add(User.Create(userId, c.Email, c.DisplayName, Roles.Courier, passwords.Hash(password, c.Email, c.DisplayName), now,
                courierId: courierId));
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var c in users.Couriers)
        {
            await locations.UpdateAsync(new CourierPosition(SeedKeys.Courier(c.Email).Value, c.Latitude, c.Longitude, now),
                cancellationToken);
        }
    }
}
