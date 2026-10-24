using System.Collections;
using PharmacyDelivery.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace PharmacyDelivery.Tests.Infrastructure;

[TestFixture]
public sealed class StartupReportTests : TestBase
{
    private static IHostEnvironment Environment()
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("Test");
        environment.SetupGet(e => e.ApplicationName).Returns("PharmacyDelivery.Api");
        environment.SetupGet(e => e.ContentRootPath).Returns("/app");
        return environment.Object;
    }

    [Test]
    [Property("TestCase", "TC-7.20.3")]
    public void Build_RendersTreeWithSourcesOfEveryValue_AndMasksSecrets()
    {
        // Two layers like appsettings.json + appsettings.Test.json: the later one wins and is reported as the source.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Checkout:DeliveryRadiusKm"] = "5",
                ["Checkout:CartTtlHours"] = "24",
                ["Auth:TokenRetentionDays"] = "30",
                ["Auth:Jwt:SigningKeyPath"] = "/keys/jwt.pem",
                ["Auth:Jwt:SigningKeyPem"] = "-----BEGIN EC PRIVATE KEY-----",
                ["Auth:Jwt:AllowEphemeralSigningKey"] = "false",
                ["Seed:UsersPassword"] = "Demo-Password-2026!",
                ["Payments:WebhookSecret"] = "whsec-123",
                ["ConnectionStrings:Postgres"] = "Host=db;Username=pharmacy_delivery;Password=very-secret;Database=pharmacy_delivery",
                ["RabbitMq:ManagementUrl"] = "http://guest:guest-pass@rabbit:15672",
                ["Cors:AllowedOrigins:0"] = "https://a.example",
                ["Cors:AllowedOrigins:1"] = "https://b.example",
            })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Checkout:DeliveryRadiusKm"] = "7" })
            .Build();
        var environmentVariables = new Hashtable { ["POSTGRES_PASSWORD"] = "pg-secret", ["PATH"] = "/usr/bin", ["GITHUB_TOKEN"] = "ghp_x" };

        var report = StartupReport.Build(configuration, Environment(), includeEnvironmentVariables: true, environmentVariables);

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.StartWith("PharmacyDelivery API "));
            Assert.That(report, Does.Contain("├─ Host").And.Contain("Environment: Test"));
            Assert.That(report, Does.Contain("DeliveryRadiusKm = 7  ← memory"), "last provider wins");
            Assert.That(report, Does.Contain("│  ├─ CartTtlHours = 24"), "tree indentation");
            Assert.That(report, Does.Contain("TokenRetentionDays = 30").And.Contain("SigningKeyPath = /keys/jwt.pem"), "not secrets");
            Assert.That(report, Does.Contain("UsersPassword = ***").And.Contain("WebhookSecret = ***").And.Contain("SigningKeyPem = ***"));
            Assert.That(report, Does.Contain("AllowEphemeralSigningKey = false"), "flags are not secrets");
            Assert.That(report, Does.Contain("Password=***").And.Not.Contain("very-secret"), "connection string password");
            Assert.That(report, Does.Contain("guest:***@rabbit").And.Not.Contain("guest-pass"), "URL credentials");
            Assert.That(report.IndexOf("0 = https://a.example", StringComparison.Ordinal),
                Is.LessThan(report.IndexOf("1 = https://b.example", StringComparison.Ordinal)), "array items in order");
            Assert.That(report, Does.Contain("└─ Environment variables (3)"));
            Assert.That(report, Does.Contain("POSTGRES_PASSWORD = ***").And.Contain("GITHUB_TOKEN = ***").And.Contain("PATH = /usr/bin"));
            Assert.That(report,
                Does.Not.Contain("pg-secret").And.Not.Contain("Demo-Password").And.Not.Contain("whsec-123").And.Not.Contain("BEGIN EC"));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.20.3")]
    public void Build_WithoutEnvironmentVariables_OmitsTheBranch()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["A:B"] = "1" }).Build();

        var report = StartupReport.Build(configuration, Environment(), includeEnvironmentVariables: false);

        Assert.That(report, Does.Contain("└─ Configuration").And.Not.Contain("Environment variables"));
    }
}
