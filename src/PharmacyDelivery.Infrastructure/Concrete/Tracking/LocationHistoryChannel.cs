using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Bounded in-memory queue between GPS ingestion (many concurrent HTTP requests) and the single history writer.
/// When the writer falls behind, the oldest points are dropped (history is best-effort; the live position is in
/// Redis) and the drop is counted in <c>pharmacy_delivery.tracking.points_dropped</c>.
/// </summary>
public sealed class LocationHistoryChannel : ILocationHistoryQueue
{
    private readonly Channel<LocationHistoryPoint> _channel;

    public LocationHistoryChannel(IOptions<TrackingOptions> options)
    {
        _channel = Channel.CreateBounded<LocationHistoryPoint>(
            new BoundedChannelOptions(options.Value.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => PharmacyDeliveryTelemetry.TrackingPointsDropped.Add(1, new KeyValuePair<string, object?>("reason", "queue_full")));
    }

    public ChannelReader<LocationHistoryPoint> Reader => _channel.Reader;

    public bool TryEnqueue(LocationHistoryPoint point) => _channel.Writer.TryWrite(point);

    public void Complete() => _channel.Writer.TryComplete();
}
