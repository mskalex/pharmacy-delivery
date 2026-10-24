using PharmacyDelivery.Api.Configuration;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Services.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace PharmacyDelivery.Tests.Infrastructure;

[TestFixture]
public sealed class ApiInfrastructureTests : TestBase
{
    [Test]
    [Property("TestCase", "TC-7.12.1")]
    public void MaskPii_HidesEmailsPhonesTokensAndPasswords()
    {
        var masked = MaskPiiLayoutRendererWrapper.Mask(
            "user emily.johnson@example.com phone +1 212 555 0147 GET /hubs/v1/tracking?access_token=eyJabc.def&x=1 "
            + "Authorization: Bearer eyJ.abc.def body {\"password\":\"Secret-123456\"}");

        Assert.Multiple(() =>
        {
            Assert.That(masked, Does.Contain("***@example.com").And.Not.Contain("emily.johnson"));
            Assert.That(masked, Does.Not.Contain("555 0147"));
            Assert.That(masked, Does.Contain("access_token=***").And.Not.Contain("eyJabc"));
            Assert.That(masked, Does.Contain("Bearer ***"));
            Assert.That(masked, Does.Not.Contain("Secret-123456"));
        });
    }

    [TestCase(typeof(ValidationFailedException), 400)]
    [TestCase(typeof(UnauthorizedException), 401)]
    [TestCase(typeof(ForbiddenException), 403)]
    [TestCase(typeof(NotFoundException), 404)]
    [TestCase(typeof(ConflictException), 409)]
    [TestCase(typeof(BusinessRuleException), 422)]
    [TestCase(typeof(DomainException), 422)]
    [TestCase(typeof(ServiceUnavailableException), 503)]
    [TestCase(typeof(DatabaseTimeoutException), 504)]
    [TestCase(typeof(InvalidOperationException), 500)]
    [Property("TestCase", "TC-7.13.1")]
    public void ExceptionHandler_MapsExceptionTypesToStatusCodes(Type exceptionType, int expectedStatus)
    {
        Exception exception = exceptionType switch
        {
            _ when exceptionType == typeof(ValidationFailedException) => new ValidationFailedException(new Dictionary<string, string[]>()),
            _ when exceptionType == typeof(UnauthorizedException) => new UnauthorizedException("c", "m"),
            _ when exceptionType == typeof(ForbiddenException) => new ForbiddenException("c", "m"),
            _ when exceptionType == typeof(NotFoundException) => new NotFoundException("c", "m"),
            _ when exceptionType == typeof(ConflictException) => new ConflictException("c", "m"),
            _ when exceptionType == typeof(BusinessRuleException) => new BusinessRuleException("c", "m"),
            _ when exceptionType == typeof(DomainException) => new DomainException("c", "m"),
            _ when exceptionType == typeof(ServiceUnavailableException) => new ServiceUnavailableException("m"),
            _ when exceptionType == typeof(DatabaseTimeoutException) => new DatabaseTimeoutException("m"),
            _ => new InvalidOperationException("boom"),
        };

        var (status, _, detail) = PharmacyDeliveryExceptionHandler.Map(exception);

        Assert.That(status, Is.EqualTo(expectedStatus));
        if (status == 500)
            Assert.That(detail, Does.Not.Contain("boom"), "internal details never leak to the client");
    }

    [Test]
    [Property("TestCase", "TC-7.18.2")]
    public void EnvironmentGuards_RejectDangerousCombinations()
    {
        var production = new Mock<IHostEnvironment>();
        production.SetupGet(e => e.EnvironmentName).Returns(Environments.Production);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Payments:Provider"] = "Stripe" })
            .Build();
        var guards = new EnvironmentGuards(production.Object, config);

        Assert.Multiple(() =>
        {
            Assert.That(guards.Validate(null, new PaymentsOptions { Provider = "Fake" }).Failed, Is.True, "fake PSP in production");
            Assert.That(guards.Validate(null, new AuthOptions { Jwt = { AllowEphemeralSigningKey = true } }).Failed, Is.True,
                "ephemeral key outside Development");
            Assert.That(guards.Validate(null, new SeedOptions { Mode = "Reset", AllowReset = false }).Failed, Is.True, "reset not allowed");
            Assert.That(guards.Validate(null, new SeedOptions { Mode = "None", AllowReset = true }).Failed, Is.True,
                "reset allowed with a real PSP");
            Assert.That(guards.Validate(null, new AuthOptions { Jwt = { SigningKeyPath = "/keys/k.pem" } }).Succeeded, Is.True);
        });
    }
}
