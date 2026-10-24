namespace PharmacyDelivery.Common.Abstract;

/// <summary>Abstraction over the current time so that business rules (reservation expiry, timeouts) are testable.</summary>
public interface IClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}
