using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Identity;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Write-side access to user accounts.</summary>
public interface IUserRepository
{
    /// <summary>Adds a new account.</summary>
    void Add(User user);

    /// <summary>Loads an account (tracked), or null.</summary>
    Task<User?> GetAsync(UserId id, CancellationToken cancellationToken);

    /// <summary>Account by normalized (lower-case) email (tracked), or null.</summary>
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    /// <summary>Whether an account with this normalized email exists.</summary>
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);
}
