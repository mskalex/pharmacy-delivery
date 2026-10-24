using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Api.Configuration;

public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public bool Enabled { get; set; } = true;

    [Required] public string ServiceName { get; set; } = "pharmacy-delivery-api";

    /// <summary>OTLP collector endpoint, e.g. http://aspire-dashboard:18889. Empty: no export (instrumentation stays active for tests).</summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary><c>grpc</c> or <c>http/protobuf</c>.</summary>
    [RegularExpression("^(grpc|http/protobuf)$")]
    public string OtlpProtocol { get; set; } = "grpc";

    /// <summary>Head sampling ratio of new traces (ParentBased(TraceIdRatio)).</summary>
    [Range(0, 1)]
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>Also export ILogger logs over OTLP (NLog targets stay active).</summary>
    public bool ExportLogs { get; set; } = true;
}

public sealed class SwaggerOptions
{
    public const string SectionName = "Swagger";

    public bool Enabled { get; set; }
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Origins of browser front-ends allowed to call the API. Empty: same-origin only.</summary>
    public string[] AllowedOrigins { get; set; } = [];
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>Send Strict-Transport-Security (TLS is terminated by the ingress in front of the API).</summary>
    public bool UseHsts { get; set; }

    /// <summary>
    /// Trust X-Forwarded-For/Proto from any address. Off by default (only loopback proxies are trusted): enabling it
    /// without a proxy in front lets clients spoof their IP for rate limiting. Used by tests and behind a known ingress.
    /// </summary>
    public bool TrustAllForwardedHeaders { get; set; }
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitRule Global { get; set; } = new() { PermitLimit = 300, WindowSeconds = 60 };

    public RateLimitRule Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RateLimitRule Checkout { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    public RateLimitRule Tracking { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };
}

public sealed class RateLimitRule
{
    [Range(1, 1_000_000)] public int PermitLimit { get; set; }

    [Range(1, 3600)] public int WindowSeconds { get; set; }
}

/// <summary>
/// Startup guards against dangerous configuration combinations (DESIGN.md, environments). Behaviour is driven by
/// settings, the environment name only selects the settings file — these checks make a wrong file fail fast.
/// </summary>
public sealed class EnvironmentGuards(IHostEnvironment environment, IConfiguration configuration)
    : IValidateOptions<PaymentsOptions>, IValidateOptions<AuthOptions>, IValidateOptions<SeedOptions>
{
    private bool IsLocal => environment.IsDevelopment() || environment.IsEnvironment("IntegrationTests");

    public ValidateOptionsResult Validate(string? name, PaymentsOptions options) =>
        environment.IsProduction() && options.Provider == "Fake"
            ? ValidateOptionsResult.Fail("Payments:Provider=Fake is not allowed in Production.")
            : ValidateOptionsResult.Success;

    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();
        if (options.Jwt.AllowEphemeralSigningKey && !IsLocal)
            failures.Add("Auth:Jwt:AllowEphemeralSigningKey is allowed only in Development.");

        if (options.Mode == "Local"
            && string.IsNullOrWhiteSpace(options.Jwt.SigningKeyPem)
            && string.IsNullOrWhiteSpace(options.Jwt.SigningKeyPath)
            && !options.Jwt.AllowEphemeralSigningKey)
        {
            failures.Add("Auth:Jwt:SigningKeyPem or Auth:Jwt:SigningKeyPath must be configured.");
        }

        if (options.Mode == "Oidc" && string.IsNullOrWhiteSpace(options.Oidc.Authority))
            failures.Add("Auth:Oidc:Authority is required when Auth:Mode=Oidc.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    public ValidateOptionsResult Validate(string? name, SeedOptions options)
    {
        var failures = new List<string>();
        if (options.Mode == "Reset" && !options.AllowReset)
            failures.Add("Seed:Mode=Reset requires Seed:AllowReset=true (Development/Test only).");

        if (options.AllowReset && configuration["Payments:Provider"] == "Stripe")
            failures.Add("Seed:AllowReset must not be enabled together with a real payment provider.");

        if (!IsLocal && options.Mode != "None" && string.IsNullOrWhiteSpace(options.AdminPassword) &&
            string.IsNullOrWhiteSpace(options.UsersPassword))
        {
            failures.Add("Seed:AdminPassword is required outside Development when seeding is enabled.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
