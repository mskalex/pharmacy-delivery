using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class UserQueries(PharmacyDeliveryDbContext db) : IUserQueries
{
    public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        var id = new UserId(userId);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null
            ? null
            : new UserProfile(
                user.Id.Value,
                user.Email,
                user.DisplayName,
                user.Role,
                user.PharmacyId?.Value,
                user.CourierId?.Value,
                user.Addresses.Select(a => new AddressInput(a.Street, a.City, a.PostalCode, a.Latitude, a.Longitude)).ToList());
    }

    public async Task<IReadOnlyList<SessionInfo>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionId,
        CancellationToken cancellationToken)
    {
        var id = new UserId(userId);
        return await db.AuthSessions.AsNoTracking()
            .Where(s => s.UserId == id && s.RevokedAt == null)
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionInfo(s.Id, s.UserAgent, s.IpAddress, s.CreatedAt, s.LastUsedAt, s.Id == currentSessionId))
            .ToListAsync(cancellationToken);
    }
}
