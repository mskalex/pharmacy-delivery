using System.Net;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Tests.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PharmacyDelivery.Tests.Integration;

[TestFixture]
[UseSeed(TestSeedProfiles.Minimal)]
public sealed class AuthAndSecurityTests : IntegrationTestBase
{
    [Test]
    [Property("TestCase", "TC-7.10.5")]
    public async Task Refresh_RotatesTokens_ReuseRevokesWholeSession()
    {
        using var client = await LoginAsync(TestWorld.Customer);
        var original = client.RefreshToken!;

        var rotated = await client.PostAsync("/api/v1/auth/refresh", new { refreshToken = original });
        Assert.That(rotated.Status, Is.EqualTo(HttpStatusCode.OK));

        var reuse = await client.PostAsync("/api/v1/auth/refresh", new { refreshToken = original });
        var afterReuse = await client.PostAsync("/api/v1/auth/refresh", new { refreshToken = rotated.Text("refreshToken") });

        Assert.Multiple(() =>
        {
            Assert.That((reuse.Status, reuse.Code), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.RefreshTokenReused)));
            Assert.That(afterReuse.Status, Is.EqualTo(HttpStatusCode.Unauthorized), "the whole session (token family) is revoked");
        });
    }

    [Test]
    [Property("TestCase", "TC-7.10.6")]
    public async Task Logout_RevokesAccessTokenImmediately()
    {
        using var client = await LoginAsync(TestWorld.Customer);
        Assert.That((await client.GetAsync("/api/v1/auth/me")).Status, Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await client.PostAsync("/api/v1/auth/logout")).Status, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.That((await client.GetAsync("/api/v1/auth/me")).Status, Is.EqualTo(HttpStatusCode.Unauthorized),
            "token not expired yet, but revoked");
    }

    [Test]
    [Property("TestCase", "TC-7.10.7")]
    public async Task Tokens_AreStoredOnlyAsHashes()
    {
        using var client = await LoginAsync(TestWorld.Customer);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDeliveryDbContext>();

        var hashes = await db.RefreshTokens.AsNoTracking().Select(t => t.TokenHash).ToListAsync();

        Assert.That(hashes, Does.Contain(TokenHashing.Sha256Hex(client.RefreshToken!)));
        Assert.That(hashes, Does.Not.Contain(client.RefreshToken));
    }

    [Test]
    [Property("TestCase", "TC-7.11.7")]
    public async Task Register_WeakPasswordIs400_DuplicateEmailIs409()
    {
        using var anonymous = Anonymous();

        var weak = await anonymous.PostAsync("/api/v1/auth/register",
            new { email = "new.user@test.local", password = "unbelievable", displayName = "New User" });
        var duplicate = await anonymous.PostAsync("/api/v1/auth/register",
            new { email = TestWorld.Customer, password = "Long-Secure-Password-1", displayName = "Someone" });

        Assert.Multiple(() =>
        {
            Assert.That(weak.Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(weak.Node("errors.password")!.ToJsonString(), Does.Contain(ErrorCodes.PasswordCommon));
            Assert.That((duplicate.Status, duplicate.Code), Is.EqualTo((HttpStatusCode.Conflict, ErrorCodes.EmailTaken)));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.11.8")]
    public async Task FiveFailedLogins_LockTheAccount_EvenForCorrectPassword()
    {
        using var anonymous = Anonymous();
        for (var i = 0; i < 5; i++)
            await anonymous.PostAsync("/api/v1/auth/login", new { email = TestWorld.OtherCustomer, password = "wrong-password-" + i });

        var correct = await anonymous.PostAsync("/api/v1/auth/login",
            new { email = TestWorld.OtherCustomer, password = TestWorld.Password });

        Assert.That((correct.Status, correct.Code), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.InvalidCredentials)));
    }

    [Test]
    [Property("TestCase", "TC-7.12.3")]
    public async Task Authorization_401WithoutToken_403ForWrongRole_404ForForeignOrder()
    {
        using var anonymous = Anonymous();
        using var customer = await LoginAsync(TestWorld.Customer);
        using var other = await LoginAsync(TestWorld.OtherCustomer);
        var orderId = (await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)])).Id("orderId");

        var noToken = await anonymous.GetAsync("/api/v1/cart");
        var wrongRole = await customer.GetAsync("/api/v1/admin/audit");
        var foreign = await other.GetAsync($"/api/v1/orders/{orderId}");

        Assert.Multiple(() =>
        {
            Assert.That((noToken.Status, noToken.Code), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized)));
            Assert.That(wrongRole.Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That((foreign.Status, foreign.Code), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.OrderNotFound)));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.12.4")]
    public async Task Responses_CarrySecurityHeadersAndTraceId()
    {
        using var anonymous = Anonymous();

        var response = await anonymous.GetAsync("/api/v1/catalog/products");

        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.GetValues("X-Content-Type-Options"), Does.Contain("nosniff"));
            Assert.That(response.Headers.GetValues("X-Frame-Options"), Does.Contain("DENY"));
            Assert.That(string.Join(" ", response.Headers.GetValues("Content-Security-Policy")), Does.Contain("default-src 'none'"));
            Assert.That(response.Headers.GetValues("X-Trace-Id").Single(), Has.Length.EqualTo(32));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.12.5")]
    public async Task LoginRateLimit_Returns429WithRetryAfter()
    {
        using var client = Anonymous("192.0.2.77");
        ApiResponse? last = null;
        for (var i = 0; i < 11; i++)
            last = await client.PostAsync("/api/v1/auth/login", new { email = "nobody@test.local", password = "whatever-123456" });

        Assert.That((last!.Status, last.Code), Is.EqualTo((HttpStatusCode.TooManyRequests, ErrorCodes.RateLimited)));
        Assert.That(last.Headers.RetryAfter, Is.Not.Null);
    }
}
