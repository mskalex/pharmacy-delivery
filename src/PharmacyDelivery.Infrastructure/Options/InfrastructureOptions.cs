using System.ComponentModel.DataAnnotations;

namespace PharmacyDelivery.Infrastructure.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>Prefix of every key written by the application (reset deletes only these keys).</summary>
    [Required]
    public string KeyPrefix { get; set; } = "pharmacy-delivery:";
}

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required] public string Host { get; set; } = "localhost";

    [Range(1, 65535)] public ushort Port { get; set; } = 5672;

    [Required] public string VirtualHost { get; set; } = "/";

    [Required] public string Username { get; set; } = "guest";

    /// <summary>Secret — environment variable only.</summary>
    [Required]
    public string Password { get; set; } = "guest";

    /// <summary>Management API, used only to purge queues on seed reset.</summary>
    public string ManagementUrl { get; set; } = "http://localhost:15672";
}

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary><c>None</c>, <c>IfEmpty</c> or <c>Reset</c> (delete all data and seed again).</summary>
    [Required]
    [RegularExpression("^(None|IfEmpty|Reset)$")]
    public string Mode { get; set; } = "None";

    /// <summary>Data template, chosen by string; classes are marked with <c>[SeedProfile("…")]</c>.</summary>
    [Required]
    public string Profile { get; set; } = "demo";

    /// <summary>Must be true for <c>Reset</c> (true only in Development and Test).</summary>
    public bool AllowReset { get; set; }

    public bool PurgeQueuesOnReset { get; set; }

    /// <summary>Password of all seeded demo users. Development/Test only; must satisfy the password policy.</summary>
    public string? UsersPassword { get; set; }

    /// <summary>Password of the admin created by the <c>empty</c> profile; required outside Development.</summary>
    public string? AdminPassword { get; set; }
}

public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    public JobSettings ReservationExpiry { get; set; } = new() { IntervalSeconds = 30, BatchSize = 100 };

    public JobSettings AcceptanceTimeout { get; set; } = new() { IntervalSeconds = 20, BatchSize = 50 };

    public JobSettings DispatchRetry { get; set; } = new() { IntervalSeconds = 15, BatchSize = 50 };

    public JobSettings PartitionMaintenance { get; set; } = new() { IntervalSeconds = 3600, BatchSize = 1 };

    public JobSettings RetentionCleanup { get; set; } = new() { IntervalSeconds = 3600, BatchSize = 5000 };

    public JobSettings StockSync { get; set; } = new() { IntervalSeconds = 600, BatchSize = 4 };
}

public sealed class JobSettings
{
    public bool Enabled { get; set; } = true;

    [Range(1, 86_400)] public int IntervalSeconds { get; set; } = 60;

    /// <summary>Batch size; for <c>StockSync</c> it is the degree of parallelism.</summary>
    [Range(1, 100_000)]
    public int BatchSize { get; set; } = 100;
}
