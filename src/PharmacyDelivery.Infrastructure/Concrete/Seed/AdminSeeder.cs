using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Administrator account; part of every template.</summary>
[SeedProfile("empty", Order = 10)]
[SeedProfile("demo", Order = 10)]
[SeedProfile("large", Order = 10)]
public sealed class AdminSeeder(
    PharmacyDeliveryDbContext db,
    SeedDataSource data,
    SeedPasswordHasher passwords,
    IClock clock,
    IOptions<SeedOptions> options) : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var admin = data.Read<UsersJson>("users.json").Admin;
        var password = SeedKeys.FirstNonEmpty(options.Value.AdminPassword, options.Value.UsersPassword)
                       ?? throw new InvalidOperationException("Seed:AdminPassword (or Seed:UsersPassword) must be configured.");

        db.Users.Add(User.Create(
            SeedKeys.User(admin.Email),
            admin.Email,
            admin.DisplayName,
            Roles.Admin,
            passwords.Hash(password, admin.Email, admin.DisplayName),
            clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }
}
