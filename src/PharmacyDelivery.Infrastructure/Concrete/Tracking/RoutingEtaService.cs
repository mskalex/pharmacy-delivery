using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Geo;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Stand-in for a routing API: straight-line distance × road factor at an average city speed. The semaphore
/// models the concurrency limit a real routing provider would impose (requests above the limit wait).
/// </summary>
public sealed class RoutingEtaService(IOptions<EtaOptions> options) : IEtaService, IDisposable
{
    private readonly EtaOptions _options = options.Value;
    private readonly SemaphoreSlim _throttle = new(options.Value.MaxConcurrentRequests, options.Value.MaxConcurrentRequests);

    public async Task<TimeSpan> EstimateTravelTimeAsync(GeoPoint from, GeoPoint to, CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            var roadKm = Haversine.DistanceKm(from.Latitude, from.Longitude, to.Latitude, to.Longitude) * _options.RoadFactor;
            return TimeSpan.FromHours(roadKm / _options.AverageSpeedKmh);
        }
        finally
        {
            _throttle.Release();
        }
    }

    public void Dispose() => _throttle.Dispose();
}
