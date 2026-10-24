using System.ComponentModel.DataAnnotations;

namespace PharmacyDelivery.Services.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary><c>Local</c> — this API issues tokens; <c>Oidc</c> — an external identity provider does.</summary>
    [Required]
    [RegularExpression("^(Local|Oidc)$")]
    public string Mode { get; set; } = "Local";

    [Required] public string Issuer { get; set; } = "pharmacy-delivery-api";

    [Required] public string Audience { get; set; } = "pharmacy-delivery";

    [Range(1, 120)] public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)] public int CustomerRefreshTokenDays { get; set; } = 14;

    [Range(1, 365)] public int CourierRefreshTokenDays { get; set; } = 30;

    [Range(1, 100)] public int MaxFailedAttempts { get; set; } = 5;

    [Range(1, 1440)] public int LockoutMinutes { get; set; } = 15;

    /// <summary>Expired sessions and tokens are deleted this many days after expiry.</summary>
    [Range(1, 365)]
    public int TokenRetentionDays { get; set; } = 7;

    [Required] public PasswordPolicyOptions Password { get; set; } = new();

    [Required] public JwtSigningOptions Jwt { get; set; } = new();

    [Required] public OidcOptions Oidc { get; set; } = new();
}

public sealed class PasswordPolicyOptions
{
    [Range(8, 128)] public int MinLength { get; set; } = 12;

    [Range(16, 1024)] public int MaxLength { get; set; } = 128;

    public bool RequireUppercase { get; set; }

    public bool RequireLowercase { get; set; }

    public bool RequireDigit { get; set; }

    public bool RequireNonAlphanumeric { get; set; }

    /// <summary>Number of previous passwords that cannot be reused.</summary>
    [Range(0, 24)]
    public int HistoryCount { get; set; } = 5;

    /// <summary>Forced periodic change; 0 disables it (recommended by NIST SP 800-63B).</summary>
    [Range(0, 3650)]
    public int MaxAgeDays { get; set; }
}

public sealed class JwtSigningOptions
{
    /// <summary>ES256 private key (PKCS#8 PEM). Secret — environment variable only.</summary>
    public string? SigningKeyPem { get; set; }

    /// <summary>Path to a PEM file with the ES256 private key (used by docker-compose).</summary>
    public string? SigningKeyPath { get; set; }

    [Required] public string KeyId { get; set; } = "pharmacy-delivery-es256-1";

    /// <summary>Generate a throw-away key when none is configured. Development only (validated on startup).</summary>
    public bool AllowEphemeralSigningKey { get; set; }
}

public sealed class OidcOptions
{
    public string? Authority { get; set; }

    public string? Audience { get; set; }

    /// <summary>Claim that carries the application role in tokens issued by the identity provider.</summary>
    public string RoleClaim { get; set; } = "role";

    public bool RequireHttpsMetadata { get; set; } = true;
}
