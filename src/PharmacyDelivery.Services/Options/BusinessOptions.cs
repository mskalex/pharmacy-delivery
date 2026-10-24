using System.ComponentModel.DataAnnotations;

namespace PharmacyDelivery.Services.Options;

// Business parameters are configuration, not code. Every key is documented in docs/configuration.md
// (a test fails if one is missing) and validated on startup (ValidateDataAnnotations + ValidateOnStart).

public sealed class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>Maximum distance between the delivery address and a pharmacy.</summary>
    [Range(0.5, 50)]
    public double DeliveryRadiusKm { get; set; } = 5;

    /// <summary>How many ranked pharmacies are tried before checkout gives up.</summary>
    [Range(1, 10)]
    public int MaxPharmacyAttempts { get; set; } = 3;

    /// <summary>Unpaid reservation lifetime.</summary>
    [Range(1, 120)]
    public int ReservationTtlMinutes { get; set; } = 15;

    [Range(1, 100)] public int MaxCartItems { get; set; } = 30;

    [Range(1, 100)] public int MaxQuantityPerItem { get; set; } = 10;

    [Range(1, 720)] public int CartTtlHours { get; set; } = 24;

    /// <summary>IANA time zone used to evaluate pharmacy opening hours.</summary>
    [Required]
    public string TimeZone { get; set; } = "America/New_York";
}

public sealed class PharmacyOptions
{
    public const string SectionName = "Pharmacy";

    /// <summary>Time a pharmacy has to accept an order before it is reassigned.</summary>
    [Range(1, 60)]
    public int AcceptanceTimeoutMinutes { get; set; } = 3;

    /// <summary>Ranking: extra minutes per order already in the pharmacy queue.</summary>
    [Range(0, 30)]
    public double MinutesPerQueuedOrder { get; set; } = 2;

    /// <summary>Ranking and ETA: base time to pick and pack an order.</summary>
    [Range(0, 60)]
    public double BasePreparationMinutes { get; set; } = 5;
}

public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    [Range(0.5, 50)] public double SearchRadiusKm { get; set; } = 7;

    [Range(1, 100)] public int MaxCandidates { get; set; } = 10;

    /// <summary>A courier whose last position is older than this is treated as offline.</summary>
    [Range(10, 3600)]
    public int MaxPositionAgeSeconds { get; set; } = 120;

    /// <summary>TTL of the Redis lock held on a courier during assignment.</summary>
    [Range(1, 120)]
    public int CourierLockSeconds { get; set; } = 15;
}

public sealed class TrackingOptions
{
    public const string SectionName = "Tracking";

    [Range(1, 500)] public int MaxBatchSize { get; set; } = 50;

    /// <summary>Points implying a higher speed than this are dropped as GPS outliers.</summary>
    [Range(10, 300)]
    public double MaxSpeedKmh { get; set; } = 120;

    /// <summary>Points older than this are rejected (stale buffer flushed by the app after a long outage).</summary>
    [Range(1, 1440)]
    public int MaxPointAgeMinutes { get; set; } = 10;

    /// <summary>Minimum interval between SignalR pushes for one order.</summary>
    [Range(100, 60000)]
    public int PushIntervalMs { get; set; } = 1000;

    /// <summary>How often the ETA is recalculated while tracking.</summary>
    [Range(1, 600)]
    public int EtaRecalcSeconds { get; set; } = 30;

    /// <summary>Capacity of the in-memory queue between the ingest endpoint and the history writer.</summary>
    [Range(100, 1_000_000)]
    public int ChannelCapacity { get; set; } = 10_000;

    [Range(1, 10_000)] public int HistoryBatchSize { get; set; } = 500;

    [Range(100, 60_000)] public int HistoryFlushIntervalMs { get; set; } = 1000;

    [Range(1, 365)] public int HistoryRetentionDays { get; set; } = 30;

    [Range(1, 60)] public int PartitionsAheadDays { get; set; } = 7;
}

public sealed class EtaOptions
{
    public const string SectionName = "Eta";

    [Range(5, 100)] public double AverageSpeedKmh { get; set; } = 25;

    /// <summary>Straight-line distance is multiplied by this factor to approximate the road distance.</summary>
    [Range(1, 3)]
    public double RoadFactor { get; set; } = 1.3;

    /// <summary>Concurrency limit for routing calls (the routing API is rate limited).</summary>
    [Range(1, 256)]
    public int MaxConcurrentRequests { get; set; } = 8;
}

public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary><c>Fake</c> (Development/Test) or <c>Stripe</c> (Staging sandbox/Production).</summary>
    [Required]
    [RegularExpression("^(Fake|Stripe)$")]
    public string Provider { get; set; } = "Fake";

    /// <summary>HMAC secret shared with the PSP. Secret — environment variable only.</summary>
    [Required]
    [MinLength(16)]
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Accepted clock skew of the webhook timestamp (replay protection).</summary>
    [Range(10, 3600)]
    public int SignatureToleranceSeconds { get; set; } = 300;

    /// <summary>Base URL the fake PSP calls back (the API itself).</summary>
    public string FakeWebhookBaseUrl { get; set; } = "http://localhost:8080";
}
