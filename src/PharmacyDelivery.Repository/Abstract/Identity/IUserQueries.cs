using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Read side of users and sessions.</summary>
public interface IUserQueries
{
    /// <summary>Profile with saved delivery addresses, or null.</summary>
    Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Active sessions of a user; the current one is flagged.</summary>
    Task<IReadOnlyList<SessionInfo>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken);
}
