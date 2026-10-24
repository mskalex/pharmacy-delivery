using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Registration, login, token refresh, sessions and password change (Auth:Mode = Local).</summary>
[Route(RoutePrefix + "auth")]
public sealed class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>Registers a customer account and signs it in.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [Audited("auth.register")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TokenResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var tokens = await auth.RegisterAsync(request.Email, request.Password, request.DisplayName, Client, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, tokens.ToResponse());
    }

    /// <summary>Signs in with email and password. The same 401 is returned for an unknown email, a wrong password and a locked account.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [Audited("auth.login")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        (await auth.LoginAsync(request.Email, request.Password, Client, cancellationToken)).ToResponse();

    /// <summary>Exchanges a refresh token for a new token pair (rotation; reuse of an old token revokes the session).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [Audited("auth.refresh")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        (await auth.RefreshAsync(request.RefreshToken, Client, cancellationToken)).ToResponse();

    /// <summary>Ends the current session; its access token stops working immediately.</summary>
    [HttpPost("logout")]
    [Audited("auth.logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(CurrentUser.UserId, CurrentUser.SessionId ?? Guid.Empty, cancellationToken);
        return NoContent();
    }

    /// <summary>Ends all sessions of the user on all devices.</summary>
    [HttpPost("logout-all")]
    [Audited("auth.logout_all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        await auth.LogoutAllAsync(CurrentUser.UserId, cancellationToken);
        return NoContent();
    }

    /// <summary>Profile of the signed-in user, including saved delivery addresses.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProfileResponse>> Me(CancellationToken cancellationToken) =>
        (await auth.GetProfileAsync(CurrentUser.UserId, cancellationToken)).ToResponse();

    /// <summary>Active sessions (devices) of the user.</summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<SessionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SessionResponse>>> Sessions(CancellationToken cancellationToken) =>
        (await auth.GetSessionsAsync(CurrentUser.UserId, CurrentUser.SessionId, cancellationToken)).Select(s => s.ToResponse()).ToList();

    /// <summary>Ends one session of the user.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [Audited("auth.session_revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        await auth.RevokeSessionAsync(CurrentUser.UserId, sessionId, cancellationToken);
        return NoContent();
    }

    /// <summary>Changes the password (policy and history are checked); all other sessions are ended.</summary>
    [HttpPost("change-password")]
    [Audited("auth.change_password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await auth.ChangePasswordAsync(CurrentUser.UserId, CurrentUser.SessionId ?? Guid.Empty, request.CurrentPassword,
            request.NewPassword, cancellationToken);
        return NoContent();
    }
}
