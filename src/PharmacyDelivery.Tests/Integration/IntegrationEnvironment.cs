using System.Diagnostics;
using System.Globalization;
using PharmacyDelivery.Infrastructure.Concrete;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Tests.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace PharmacyDelivery.Tests.Integration;

/// <summary>
/// Starts PostgreSQL/PostGIS, Redis and RabbitMQ in containers once for all integration tests, plus one API host
/// (WebApplicationFactory, environment "IntegrationTests"). Each test loads its data with <see cref="UseSeedAttribute"/>.
/// Requires a running Docker engine.
/// </summary>
[SetUpFixture]
public sealed class IntegrationEnvironment
{
    public const string RabbitUser = "pharmacy_delivery";
    public const string RabbitPassword = "integration-tests";
    public const string WebhookSecret = "integration-tests-webhook-secret";

    private static PostgreSqlContainer? _postgres;
    private static RedisContainer? _redis;
    private static RabbitMqContainer? _rabbit;

    public static PharmacyDeliveryApiFactory Factory { get; private set; } = null!;

    public static string PostgresConnectionString => _postgres!.GetConnectionString();

    /// <summary>Spans recorded by the in-memory OpenTelemetry exporter.</summary>
    public static List<Activity> Spans { get; } = [];

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _postgres = new PostgreSqlBuilder("postgis/postgis:17-3.5").WithDatabase("pharmacy_delivery").WithUsername("pharmacy_delivery")
            .WithPassword("integration").Build();
        _redis = new RedisBuilder("redis:7.2-alpine").Build();
        _rabbit = new RabbitMqBuilder("rabbitmq:4.1-management-alpine").WithUsername(RabbitUser).WithPassword(RabbitPassword).Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbit.StartAsync());

        Factory = new PharmacyDeliveryApiFactory(_postgres.GetConnectionString(), _redis.GetConnectionString(),
            new Uri(_rabbit.GetConnectionString()));
        _ = Factory.Server; // build the host: migrations run, DI is validated (ValidateOnBuild)
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();

        if (_postgres is not null)
            await _postgres.DisposeAsync();

        if (_redis is not null)
            await _redis.DisposeAsync();

        if (_rabbit is not null)
            await _rabbit.DisposeAsync();
    }

    /// <summary>Deletes all data and loads a seed template (same code path as Seed:Mode=Reset).</summary>
    public static async Task ResetAsync(string profile)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDataMaintenance>().ResetAsync(profile, CancellationToken.None);
    }
}

public sealed class PharmacyDeliveryApiFactory(string postgres, string redis, Uri rabbit) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:Postgres"] = postgres,
            ["ConnectionStrings:Redis"] = redis,
            ["RabbitMq:Host"] = rabbit.Host,
            ["RabbitMq:Port"] = rabbit.Port.ToString(CultureInfo.InvariantCulture),
            ["RabbitMq:Username"] = IntegrationEnvironment.RabbitUser,
            ["RabbitMq:Password"] = IntegrationEnvironment.RabbitPassword,
            ["Auth:Jwt:AllowEphemeralSigningKey"] = "true",
            ["Payments:Provider"] = "Fake",
            ["Payments:WebhookSecret"] = IntegrationEnvironment.WebhookSecret,
            ["Payments:FakeWebhookBaseUrl"] = "http://localhost",
            ["Seed:Mode"] = "None",
            ["Seed:AllowReset"] = "true",
            ["Seed:UsersPassword"] = TestWorld.Password,
            ["OpenTelemetry:Enabled"] = "true",
            ["OpenTelemetry:OtlpEndpoint"] = string.Empty,
            ["OpenTelemetry:SamplingRatio"] = "1",
            ["Swagger:Enabled"] = "true",
            ["Admin:MaintenanceEndpointsEnabled"] = "true",
            ["ErrorHandling:IncludeExceptionDetails"] = "false",
            ["Security:UseHsts"] = "false",
            ["Security:TrustAllForwardedHeaders"] = "true",
            ["Database:LockTimeoutMs"] = "1000",
            ["RateLimiting:Global:PermitLimit"] = "100000",
            ["RateLimiting:Checkout:PermitLimit"] = "100000",
            ["RateLimiting:Tracking:PermitLimit"] = "100000",
        };
        string[] jobs =
            ["ReservationExpiry", "AcceptanceTimeout", "DispatchRetry", "StockSync", "RetentionCleanup", "PartitionMaintenance"];
        foreach (var job in jobs)
            settings[$"Jobs:{job}:Enabled"] = "false";

        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            // Test data templates live in this assembly ([SeedProfile("minimal")] etc.).
            services.AddSingleton(new SeederRegistry([typeof(DataInitializer).Assembly, typeof(IntegrationEnvironment).Assembly]));

            // The fake PSP calls the webhook through the in-memory test server.
            services.AddHttpClient<FakePspSimulator>(client => client.BaseAddress = new Uri("http://localhost"))
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());

            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(IntegrationEnvironment.Spans));
        });
    }
}
