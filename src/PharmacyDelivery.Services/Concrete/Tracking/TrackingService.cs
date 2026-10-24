using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Geo;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// GPS ingestion hot path: validate and filter points → latest position to Redis (dispatch + live view) →
/// history to a bounded in-memory channel (batched writer) → throttled push to the customer with a fresh ETA.
/// The courier position is exposed to the customer only while the order is <c>InDelivery</c>.
/// </summary>
public sealed class TrackingService(
    ICourierLocationStore locations,
    IDeliveryQueries deliveryQueries,
    IOrderQueries orderQueries,
    ILocationHistoryQueue historyQueue,
    IOrderNotifier notifier,
    IEtaService eta,
    TrackingPushThrottle throttle,
    IClock clock,
    IOptions<TrackingOptions> options) : ITrackingService
{
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(30);
    private readonly TrackingOptions _options = options.Value;

    public async Task<LocationIngestResult> IngestAsync(Guid courierId, IReadOnlyList<LocationPointInput> points,
        CancellationToken cancellationToken)
    {
        using var activity = PharmacyDeliveryTelemetry.StartActivity("tracking.ingest");
        if (points.Count > _options.MaxBatchSize)
        {
            throw ValidationFailedException.ForField("points", ErrorCodes.ValidationFailed,
                $"At most {_options.MaxBatchSize} points per batch.");
        }

        var now = clock.UtcNow;
        var last = await locations.GetAsync(courierId, cancellationToken);
        var accepted = new List<LocationPointInput>(points.Count);
        var dropped = 0;

        foreach (var point in points.OrderBy(p => p.RecordedAt))
        {
            var reason = Reject(point, last, now);
            if (reason is not null)
            {
                dropped++;
                PharmacyDeliveryTelemetry.TrackingPointsDropped.Add(1, new KeyValuePair<string, object?>("reason", reason));
                continue;
            }

            accepted.Add(point);
            last = new CourierPosition(courierId, point.Latitude, point.Longitude, point.RecordedAt);
        }

        if (accepted.Count == 0)
            return new LocationIngestResult(0, dropped);

        await locations.UpdateAsync(last!, cancellationToken);
        var active = await deliveryQueries.GetActiveRefForCourierAsync(courierId, cancellationToken);

        foreach (var point in accepted)
        {
            var queued = historyQueue.TryEnqueue(new LocationHistoryPoint(
                courierId,
                active?.DeliveryId,
                point.Latitude,
                point.Longitude,
                point.SpeedKmh,
                point.RecordedAt));
            if (!queued)
                PharmacyDeliveryTelemetry.TrackingPointsDropped.Add(1, new KeyValuePair<string, object?>("reason", "queue_full"));
        }

        PharmacyDeliveryTelemetry.TrackingPointsReceived.Add(accepted.Count);

        if (active is { Status: DeliveryStatus.InDelivery })
            await PushAsync(active, last!, now, cancellationToken);

        activity?.SetTag("accepted", accepted.Count);
        activity?.SetTag("dropped", dropped);
        return new LocationIngestResult(accepted.Count, dropped);
    }

    public async Task<TrackingSnapshot> GetSnapshotAsync(Guid orderId, ICurrentUser user, CancellationToken cancellationToken)
    {
        var access = await orderQueries.GetAccessInfoAsync(orderId, cancellationToken);
        OrderAccess.EnsureCanView(access, user, orderId);

        var details = await orderQueries.GetDetailsAsync(orderId, cancellationToken)
                      ?? throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {orderId} was not found.");

        CourierPosition? courier = null;
        var estimate = details.EstimatedDeliveryAt;
        if (details is { Status: OrderStatus.InDelivery, CourierId: { } courierId })
        {
            courier = await locations.GetAsync(courierId, cancellationToken);
            if (courier is not null)
            {
                var travel = await eta.EstimateTravelTimeAsync(
                    new GeoPoint(courier.Latitude, courier.Longitude),
                    new GeoPoint(details.DeliveryAddress.Latitude, details.DeliveryAddress.Longitude),
                    cancellationToken);
                estimate = clock.UtcNow.Add(travel);
            }
        }

        return new TrackingSnapshot(details.Id, details.Status, details.Pharmacy, details.DeliveryAddress, courier, estimate);
    }

    public async Task<bool> CanSubscribeAsync(Guid orderId, ICurrentUser user, CancellationToken cancellationToken)
    {
        var access = await orderQueries.GetAccessInfoAsync(orderId, cancellationToken);
        return access is not null && OrderAccess.CanView(access, user);
    }

    private string? Reject(LocationPointInput point, CourierPosition? last, DateTimeOffset now)
    {
        if (point.RecordedAt > now + MaxClockSkew)
            return "future";

        if (point.RecordedAt < now.AddMinutes(-_options.MaxPointAgeMinutes))
            return "stale";

        if (last is null)
            return null;

        if (point.RecordedAt <= last.RecordedAt)
            return "out_of_order";

        var hours = (point.RecordedAt - last.RecordedAt).TotalHours;
        var km = Haversine.DistanceKm(last.Latitude, last.Longitude, point.Latitude, point.Longitude);
        return km / hours > _options.MaxSpeedKmh ? "outlier" : null;
    }

    private async Task PushAsync(ActiveDeliveryRef active, CourierPosition position, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var decision = throttle.Evaluate(
            active.OrderId,
            now,
            TimeSpan.FromMilliseconds(_options.PushIntervalMs),
            TimeSpan.FromSeconds(_options.EtaRecalcSeconds));
        if (!decision.Push)
            return;

        var estimate = decision.CachedEta;
        if (decision.RecalculateEta || estimate is null)
        {
            var travel = await eta.EstimateTravelTimeAsync(
                new GeoPoint(position.Latitude, position.Longitude),
                new GeoPoint(active.DestinationLatitude, active.DestinationLongitude),
                cancellationToken);
            estimate = now.Add(travel);
            throttle.StoreEta(active.OrderId, now, estimate.Value);
        }

        await notifier.CourierPositionChangedAsync(
            new CourierPositionNotification(active.OrderId, active.CustomerId, position.Latitude, position.Longitude, position.RecordedAt,
                estimate.Value),
            cancellationToken);
    }
}
