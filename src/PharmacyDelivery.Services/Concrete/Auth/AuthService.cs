using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Local token issuer: short-lived ES256 access tokens (registered by jti for instant revocation) and rotating
/// refresh tokens (stored as SHA-256 hashes; reuse of a rotated token revokes the whole session).
/// </summary>
public sealed class AuthService(
    IUserRepository users,
    IUserQueries userQueries,
    IAuthTokenRepository tokens,
    IAccessTokenIssuer issuer,
    IRevokedTokenCache revokedCache,
    IPasswordHasher<User> hasher,
    PasswordPolicyValidator policy,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<AuthOptions> options,
    ILogger<AuthService> logger) : IAuthService
{
    // Hash of a random password: verifying against it keeps the response time of unknown emails equal to known ones.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<User>().HashPassword(null!, TokenHashing.GenerateOpaqueToken()));

    private readonly AuthOptions _options = options.Value;

    public async Task<AuthTokens> RegisterAsync(string email, string password, string displayName, ClientInfo client,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = User.Normalize(email);
        EnsurePolicy(password, email, displayName);
        if (await users.EmailExistsAsync(normalizedEmail, cancellationToken))
            throw new ConflictException(ErrorCodes.EmailTaken, "An account with this email already exists.");

        var now = clock.UtcNow;
        var hash = hasher.HashPassword(null!, PasswordPolicyValidator.Normalize(password));
        var user = User.Create(UserId.New(), email, displayName, Roles.Customer, hash, now);
        users.Add(user);
        tokens.AddPasswordHistory(PasswordHistoryEntry.Create(user.Id, hash, now));

        var result = await StartSessionAsync(user, client, now, cancellationToken);
        logger.LogInformation("User {UserId} registered", user.Id);
        return result;
    }

    public async Task<AuthTokens> LoginAsync(string email, string password, ClientInfo client, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var normalizedPassword = PasswordPolicyValidator.Normalize(password);
        var user = await users.FindByEmailAsync(User.Normalize(email), cancellationToken);
        if (user is null)
        {
            hasher.VerifyHashedPassword(null!, DummyHash.Value, normalizedPassword);
            throw InvalidCredentials();
        }

        if (user.IsLockedOut(now))
        {
            // Same response as a wrong password: the lockout is not disclosed to an attacker.
            hasher.VerifyHashedPassword(user, user.PasswordHash, normalizedPassword);
            logger.LogWarning("Login attempt for locked-out user {UserId}", user.Id);
            throw InvalidCredentials();
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, normalizedPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now, _options.MaxFailedAttempts, TimeSpan.FromMinutes(_options.LockoutMinutes));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (user.IsLockedOut(now))
                logger.LogWarning("User {UserId} locked out after {Attempts} failed logins", user.Id, user.FailedLoginAttempts);

            throw InvalidCredentials();
        }

        if (user.IsBlocked)
            throw new ForbiddenException(ErrorCodes.AccountBlocked, "The account is blocked.");

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.UpgradePasswordHash(hasher.HashPassword(user, normalizedPassword));

        user.RegisterSuccessfulLogin();
        return await StartSessionAsync(user, client, now, cancellationToken);
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var stored = await tokens.FindRefreshTokenAsync(TokenHashing.Sha256Hex(refreshToken), cancellationToken)
                     ?? throw new UnauthorizedException(ErrorCodes.RefreshTokenInvalid, "Invalid refresh token.");
        var session = await tokens.GetSessionAsync(stored.SessionId, cancellationToken);

        if (stored.UsedAt is not null && session is not null)
        {
            // A rotated token was presented again: it was stolen or replayed. Kill the whole session.
            var revoked = await tokens.RevokeSessionsAsync(session.UserId, session.Id, null, "refresh token reuse", now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await revokedCache.MarkRevokedAsync(revoked, cancellationToken);
            logger.LogWarning("Refresh token reuse detected for session {SessionId}; session revoked", session.Id);
            throw new UnauthorizedException(ErrorCodes.RefreshTokenReused, "Refresh token was already used.");
        }

        if (!stored.IsUsable(now) || session is not { IsActive: true })
            throw new UnauthorizedException(ErrorCodes.RefreshTokenInvalid, "Invalid refresh token.");

        var user = await users.GetAsync(session.UserId, cancellationToken);
        if (user is null || user.IsBlocked)
            throw new UnauthorizedException(ErrorCodes.RefreshTokenInvalid, "Invalid refresh token.");

        session.Touch(now);
        // The refresh token row is a concurrency token (xmin): two parallel refreshes cannot both succeed.
        return await IssueAsync(user, session, now, rotated: stored, cancellationToken);
    }

    public async Task LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        await RevokeAsync(new UserId(userId), sessionId, null, "logout", cancellationToken);

    public async Task LogoutAllAsync(Guid userId, CancellationToken cancellationToken) =>
        await RevokeAsync(new UserId(userId), null, null, "logout-all", cancellationToken);

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await tokens.GetSessionAsync(sessionId, cancellationToken);
        if (session is null || session.UserId.Value != userId)
            throw new NotFoundException(ErrorCodes.SessionNotFound, $"Session {sessionId} was not found.");

        await RevokeAsync(session.UserId, sessionId, null, "session revoked by user", cancellationToken);
    }

    public async Task ChangePasswordAsync(Guid userId, Guid currentSessionId, string currentPassword, string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(new UserId(userId), cancellationToken)
                   ?? throw new NotFoundException(ErrorCodes.UserNotFound, "User was not found.");

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, PasswordPolicyValidator.Normalize(currentPassword)) ==
            PasswordVerificationResult.Failed)
        {
            throw ValidationFailedException.ForField("currentPassword", ErrorCodes.PasswordCurrentInvalid,
                "Current password is incorrect.");
        }

        EnsurePolicy(newPassword, user.Email, user.DisplayName);
        var normalized = PasswordPolicyValidator.Normalize(newPassword);
        var history = await tokens.GetRecentPasswordHashesAsync(user.Id, _options.Password.HistoryCount, cancellationToken);
        if (history.Append(user.PasswordHash)
            .Any(h => hasher.VerifyHashedPassword(user, h, normalized) != PasswordVerificationResult.Failed))
        {
            throw ValidationFailedException.ForField("newPassword", ErrorCodes.PasswordReused, "Password was used recently.");
        }

        var now = clock.UtcNow;
        var hash = hasher.HashPassword(user, normalized);
        user.ChangePasswordHash(hash, now);
        tokens.AddPasswordHistory(PasswordHistoryEntry.Create(user.Id, hash, now));

        var revoked = await tokens.RevokeSessionsAsync(user.Id, null, currentSessionId, "password changed", now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await revokedCache.MarkRevokedAsync(revoked, cancellationToken);
        logger.LogInformation("User {UserId} changed password; {Count} access tokens revoked", user.Id, revoked.Count);
    }

    public async Task BlockUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(new UserId(userId), cancellationToken)
                   ?? throw new NotFoundException(ErrorCodes.UserNotFound, "User was not found.");
        user.Block();
        await RevokeAsync(user.Id, null, null, "user blocked", cancellationToken);
        logger.LogWarning("User {UserId} blocked by an administrator", userId);
    }

    public async Task<bool> IsAccessTokenRevokedAsync(Guid jti, CancellationToken cancellationToken) =>
        await revokedCache.IsRevokedAsync(jti, cancellationToken)
        ?? await tokens.IsAccessTokenRevokedAsync(jti, cancellationToken); // Redis unavailable: fail closed via the database

    public async Task<UserProfile> GetProfileAsync(Guid userId, CancellationToken cancellationToken) =>
        await userQueries.GetProfileAsync(userId, cancellationToken)
        ?? throw new NotFoundException(ErrorCodes.UserNotFound, "User was not found.");

    public Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken) =>
        userQueries.GetActiveSessionsAsync(userId, currentSessionId, cancellationToken);

    public string HashPassword(User? user, string password)
    {
        EnsurePolicy(password, user?.Email, user?.DisplayName);
        return hasher.HashPassword(user!, PasswordPolicyValidator.Normalize(password));
    }

    private static UnauthorizedException InvalidCredentials() =>
        new(ErrorCodes.InvalidCredentials, "Invalid email or password.");

    private void EnsurePolicy(string password, string? email, string? displayName)
    {
        var violations = policy.Validate(password, email, displayName);
        if (violations.Count > 0)
        {
            throw new ValidationFailedException(
                new Dictionary<string, string[]> { ["password"] = violations.Select(v => $"{v.Code}: {v.Message}").ToArray() },
                "Password does not meet the policy.");
        }
    }

    private async Task<AuthTokens> StartSessionAsync(User user, ClientInfo client, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var session = AuthSession.Start(user.Id, client.UserAgent, client.IpAddress, now);
        tokens.AddSession(session);
        return await IssueAsync(user, session, now, rotated: null, cancellationToken);
    }

    private async Task<AuthTokens> IssueAsync(User user, AuthSession session, DateTimeOffset now, RefreshToken? rotated,
        CancellationToken cancellationToken)
    {
        var access = issuer.Issue(user, session.Id, now);
        tokens.AddAccessToken(AccessToken.Register(access.Jti, session.Id, user.Id, now, access.ExpiresAt));

        var refreshValue = TokenHashing.GenerateOpaqueToken();
        var lifetime = TimeSpan.FromDays(user.Role == Roles.Courier ? _options.CourierRefreshTokenDays : _options.CustomerRefreshTokenDays);
        var refresh = RefreshToken.Issue(session.Id, TokenHashing.Sha256Hex(refreshValue), now, lifetime);
        tokens.AddRefreshToken(refresh);
        rotated?.MarkUsed(refresh.Id, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AuthTokens(access.Token, access.ExpiresAt, refreshValue, refresh.ExpiresAt, session.Id);
    }

    private async Task RevokeAsync(UserId userId, Guid? onlySession, Guid? exceptSession, string reason,
        CancellationToken cancellationToken)
    {
        var revoked = await tokens.RevokeSessionsAsync(userId, onlySession, exceptSession, reason, clock.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await revokedCache.MarkRevokedAsync(revoked, cancellationToken);
    }
}
