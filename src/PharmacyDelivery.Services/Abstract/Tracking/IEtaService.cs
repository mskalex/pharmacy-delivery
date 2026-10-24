using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Travel time estimation (stands in for a routing API).</summary>
public interface IEtaService
{
    /// <summary>Estimated travel time between two points.</summary>
    Task<TimeSpan> EstimateTravelTimeAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken);
}
