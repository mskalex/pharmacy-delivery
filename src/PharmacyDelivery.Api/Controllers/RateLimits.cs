namespace PharmacyDelivery.Api.Controllers;

/// <summary>Rate limiter policy names (limits in docs/configuration.md, section RateLimiting).</summary>
public static class RateLimits
{
    public const string Auth = "auth";
    public const string Checkout = "checkout";
    public const string Tracking = "tracking";
}
