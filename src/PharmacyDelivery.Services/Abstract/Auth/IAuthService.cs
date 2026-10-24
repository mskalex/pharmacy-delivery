using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Accounts, sessions and tokens of the local identity provider (Auth:Mode = Local).</summary>
public interface IAuthService
{
    /// <summary>Creates a customer account (password policy applies) and signs it in.</summary>
    Task<AuthTokens> RegisterAsync(string email, string password, string displayName, ClientInfo client,
        CancellationToken cancellationToken);

    /// <summary>Signs in; unknown email, wrong password and locked account return the same error.</summary>
    Task<AuthTokens> LoginAsync(string email, string password, ClientInfo client, CancellationToken cancellationToken);

    /// <summary>Rotates the refresh token; reuse of a rotated token revokes the whole session.</summary>
    Task<AuthTokens> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken cancellationToken);

    /// <summary>Ends one session and revokes its tokens immediately.</summary>
    Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Ends all sessions of the user on all devices.</summary>
    Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Ends one session of the user chosen from the session list.</summary>
    Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Changes the password (policy and history checked) and ends all other sessions.</summary>
    Task ChangePasswordAsync(Guid userId, Guid currentSessionId, string currentPassword, string newPassword,
        CancellationToken cancellationToken);

    /// <summary>Blocks the account and revokes all its sessions and tokens immediately.</summary>
    Task BlockUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Revocation check of an access token by <c>jti</c> (Redis, falling back to the database).</summary>
    Task<bool> IsAccessTokenRevokedAsync(Guid jti, CancellationToken cancellationToken);

    /// <summary>Profile of the signed-in user.</summary>
    Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Active sessions of the signed-in user.</summary>
    Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken);

    /// <summary>Hashes a password after policy validation (used by registration and by the seed).</summary>
    string HashPassword(User? user, string password);
}
