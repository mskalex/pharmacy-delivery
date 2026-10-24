using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Per-order throttling of SignalR pushes and ETA recalculation. Shared by all requests (singleton), so access is
/// serialized with <see cref="Lock"/>; the critical section is a few dictionary operations, far cheaper than a push.
/// </summary>
public sealed class TrackingPushThrottle
{
    private const int CleanupThreshold = 10_000;
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(2);

    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public PushDecision Evaluate(Guid orderId, DateTimeOffset now, TimeSpan pushInterval, TimeSpan etaInterval)
    {
        lock (_sync)
        {
            if (_entries.Count > CleanupThreshold)
                RemoveStale(now);

            if (!_entries.TryGetValue(orderId, out var entry))
            {
                _entries[orderId] = new Entry(now, null, null);
                return new PushDecision(true, true, null);
            }

            if (now - entry.LastPush < pushInterval)
                return new PushDecision(false, false, entry.Eta);

            var recalc = entry.EtaCalculatedAt is null || now - entry.EtaCalculatedAt >= etaInterval;
            _entries[orderId] = entry with { LastPush = now };
            return new PushDecision(true, recalc, entry.Eta);
        }
    }

    public void StoreEta(Guid orderId, DateTimeOffset calculatedAt, DateTimeOffset eta)
    {
        lock (_sync)
        {
            var lastPush = _entries.TryGetValue(orderId, out var entry) ? entry.LastPush : calculatedAt;
            _entries[orderId] = new Entry(lastPush, calculatedAt, eta);
        }
    }

    private void RemoveStale(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(e => now - e.Value.LastPush > EntryLifetime).Select(e => e.Key).ToList())
        {
            _entries.Remove(key);
        }
    }

    private readonly record struct Entry(DateTimeOffset LastPush, DateTimeOffset? EtaCalculatedAt, DateTimeOffset? Eta);
}
