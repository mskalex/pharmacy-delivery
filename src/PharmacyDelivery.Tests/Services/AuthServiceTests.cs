using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Concrete;
using PharmacyDelivery.Services.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace PharmacyDelivery.Tests.Services;

[TestFixture]
public sealed class AuthServiceTests : TestBase
{
    private const string Password = "Secure-Password-2026!";
    private readonly PasswordHasher<User> _hasher = new();
    private AuthService _service = null!;
    private User _user = null!;

    private Mock<IUserRepository> Users => MockOf<IUserRepository>();

    private Mock<IAuthTokenRepository> Tokens => MockOf<IAuthTokenRepository>();

    private Mock<IRevokedTokenCache> Cache => MockOf<IRevokedTokenCache>();

    [SetUp]
    public void SetUp()
    {
        Services.AddSingleton<IPasswordHasher<User>>(_hasher);
        _service = Service<AuthService>();
        _user = User.Create(UserId.New(), "emily@example.com", "Emily", Roles.Customer, _hasher.HashPassword(null!, Password), Now);
        Users.Setup(u => u.FindByEmailAsync("emily@example.com", It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        Users.Setup(u => u.GetAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        Tokens.Setup(t => t.GetRecentPasswordHashesAsync(It.IsAny<UserId>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        Tokens.Setup(t => t.RevokeSessionsAsync(It.IsAny<UserId>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RevokedToken(Guid.NewGuid(), Now.AddMinutes(10))]);
        MockOf<IAccessTokenIssuer>().Setup(i => i.Issue(It.IsAny<User>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>()))
            .Returns(() => new IssuedAccessToken("jwt", Guid.NewGuid(), Now.AddMinutes(15)));
    }

    [Test]
    [Property("TestCase", "TC-7.10.1")]
    public async Task Login_ReturnsTokens_StoresOnlyRefreshTokenHash()
    {
        RefreshToken? stored = null;
        Tokens.Setup(t => t.AddRefreshToken(It.IsAny<RefreshToken>())).Callback<RefreshToken>(t => stored = t);

        var tokens = await _service.LoginAsync("Emily@Example.com ", Password, new ClientInfo("test", "127.0.0.1"), Ct);

        Assert.Multiple(() =>
        {
            Assert.That(tokens.AccessToken, Is.EqualTo("jwt"));
            Assert.That(stored!.TokenHash, Is.EqualTo(TokenHashing.Sha256Hex(tokens.RefreshToken)));
            Assert.That(stored.TokenHash, Is.Not.EqualTo(tokens.RefreshToken));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.11.4")]
    public void Login_UnknownEmailWrongPasswordAndLockedAccount_GiveTheSameError()
    {
        for (var i = 0; i < 5; i++)
        {
            var ex = Assert.ThrowsAsync<UnauthorizedException>(() =>
                _service.LoginAsync("emily@example.com", "wrong-password-123456", new ClientInfo(null, null), Ct));
            Assert.That(ex!.Code, Is.EqualTo(ErrorCodes.InvalidCredentials));
        }

        Assert.That(_user.IsLockedOut(Clock.UtcNow), Is.True);
        var locked = Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync("emily@example.com", Password, new ClientInfo(null, null), Ct));
        var unknown = Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync("nobody@example.com", Password, new ClientInfo(null, null), Ct));
        Assert.That((locked!.Code, unknown!.Code), Is.EqualTo((ErrorCodes.InvalidCredentials, ErrorCodes.InvalidCredentials)));

        Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.DoesNotThrowAsync(() => _service.LoginAsync("emily@example.com", Password, new ClientInfo(null, null), Ct));
    }

    [Test]
    [Property("TestCase", "TC-7.10.2")]
    public void Refresh_WithAlreadyUsedToken_RevokesSessionAndFails()
    {
        var session = AuthSession.Start(_user.Id, null, null, Now);
        var used = RefreshToken.Issue(session.Id, TokenHashing.Sha256Hex("old"), Now, TimeSpan.FromDays(14));
        used.MarkUsed(Guid.NewGuid(), Now);
        Tokens.Setup(t => t.FindRefreshTokenAsync(TokenHashing.Sha256Hex("old"), It.IsAny<CancellationToken>())).ReturnsAsync(used);
        Tokens.Setup(t => t.GetSessionAsync(session.Id, It.IsAny<CancellationToken>())).ReturnsAsync(session);

        var ex = Assert.ThrowsAsync<UnauthorizedException>(() => _service.RefreshAsync("old", new ClientInfo(null, null), Ct));

        Assert.That(ex!.Code, Is.EqualTo(ErrorCodes.RefreshTokenReused));
        Tokens.Verify(
            t => t.RevokeSessionsAsync(_user.Id, session.Id, null, It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()), Times.Once);
        Cache.Verify(c => c.MarkRevokedAsync(It.Is<IReadOnlyCollection<RevokedToken>>(r => r.Count == 1), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    [Property("TestCase", "TC-7.11.5")]
    public void ChangePassword_RejectsReuseOfRecentPassword()
    {
        Tokens.Setup(t => t.GetRecentPasswordHashesAsync(_user.Id, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_hasher.HashPassword(null!, "Old-Password-From-History-1")]);

        var ex = Assert.ThrowsAsync<ValidationFailedException>(() =>
            _service.ChangePasswordAsync(_user.Id.Value, Guid.NewGuid(), Password, "Old-Password-From-History-1", Ct));

        Assert.That(ex!.Errors["newPassword"][0], Does.StartWith(ErrorCodes.PasswordReused));
    }

    [Test]
    [Property("TestCase", "TC-7.10.3")]
    public async Task ChangePassword_RevokesOtherSessions()
    {
        var current = Guid.NewGuid();

        await _service.ChangePasswordAsync(_user.Id.Value, current, Password, "New-Password-For-The-App-2026", Ct);

        Tokens.Verify(
            t => t.RevokeSessionsAsync(_user.Id, null, current, "password changed", It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(_hasher.VerifyHashedPassword(_user, _user.PasswordHash, "New-Password-For-The-App-2026"),
            Is.Not.EqualTo(PasswordVerificationResult.Failed));
    }

    [Test]
    [Property("TestCase", "TC-7.10.4")]
    public async Task RevocationCheck_FallsBackToDatabase_WhenRedisIsUnavailable()
    {
        var jti = Guid.NewGuid();
        Cache.Setup(c => c.IsRevokedAsync(jti, It.IsAny<CancellationToken>())).ReturnsAsync((bool?)null);
        Tokens.Setup(t => t.IsAccessTokenRevokedAsync(jti, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.That(await _service.IsAccessTokenRevokedAsync(jti, Ct), Is.True);
    }
}
