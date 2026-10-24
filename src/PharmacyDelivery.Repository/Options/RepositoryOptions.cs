using System.ComponentModel.DataAnnotations;

namespace PharmacyDelivery.Repository.Options;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public bool ApplyMigrationsOnStartup { get; set; } = true;

    [Range(0, 20)] public int MaxRetryCount { get; set; } = 5;

    [Range(1, 60)] public int MaxRetryDelaySeconds { get; set; } = 5;

    [Range(1, 600)] public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum wait for a row lock in reservation transactions (<c>SET LOCAL lock_timeout</c>).</summary>
    [Range(100, 60_000)]
    public int LockTimeoutMs { get; set; } = 2000;

    /// <summary>Server-side statement timeout set on every connection.</summary>
    [Range(1000, 600_000)]
    public int StatementTimeoutMs { get; set; } = 30_000;

    /// <summary>Queries slower than this are logged as warnings.</summary>
    [Range(1, 60_000)]
    public int SlowQueryThresholdMs { get; set; } = 500;

    /// <summary>Logs SQL parameter values. Development only.</summary>
    public bool EnableSensitiveDataLogging { get; set; }
}

public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    public bool Enabled { get; set; } = true;

    /// <summary>Properties whose values are never written to the audit log.</summary>
    public string[] MaskedProperties { get; set; } = ["PasswordHash", "TokenHash"];

    [Range(1, 3650)] public int RetentionDays { get; set; } = 365;
}
