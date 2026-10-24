using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class UserRepository(PharmacyDeliveryDbContext db) : IUserRepository
{
    public void Add(User user) => db.Users.Add(user);

    public Task<User?> GetAsync(UserId id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
}
