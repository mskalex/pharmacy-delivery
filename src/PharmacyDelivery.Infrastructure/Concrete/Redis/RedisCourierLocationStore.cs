using System.Globalization;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;
using StackExchange.Redis;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Latest courier positions: a GEO index (dispatch: nearest couriers) + a hash per courier with the timestamp
/// (stale positions are ignored by dispatch). Written on every GPS batch — never touches the database.
/// </summary>
public sealed class RedisCourierLocationStore(IConnectionMultiplexer redis, RedisKeys keys) : ICourierLocationStore
{
    private static readonly TimeSpan PositionTtl = TimeSpan.FromDays(1);

    public async Task UpdateAsync(CourierPosition position, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var hashKey = keys.Courier(position.CourierId);
        var batch = db.CreateBatch();
        var tasks = new Task[]
        {
            batch.GeoAddAsync(keys.CourierGeo, position.Longitude, position.Latitude, position.CourierId.ToString("N")),
            batch.HashSetAsync(hashKey,
            [
                new HashEntry("lat", position.Latitude.ToString("R", CultureInfo.InvariantCulture)),
                new HashEntry("lng", position.Longitude.ToString("R", CultureInfo.InvariantCulture)),
                new HashEntry("at", position.RecordedAt.ToUnixTimeMilliseconds()),
            ]),
            batch.KeyExpireAsync(hashKey, PositionTtl),
        };
        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task<CourierPosition?> GetAsync(Guid courierId, CancellationToken cancellationToken)
    {
        var entries = await redis.GetDatabase().HashGetAllAsync(keys.Courier(courierId));
        return Parse(courierId, entries);
    }

    public async Task<IReadOnlyList<NearbyCourier>> FindNearestAsync(GeoPoint point, double radiusKm, int count,
        CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var results = await db.GeoSearchAsync(
            keys.CourierGeo,
            point.Longitude,
            point.Latitude,
            new GeoSearchCircle(radiusKm, GeoUnit.Kilometers),
            count,
            demandClosest: true,
            order: Order.Ascending,
            options: GeoRadiusOptions.WithDistance);

        var nearby = new List<NearbyCourier>(results.Length);
        foreach (var result in results)
        {
            if (!Guid.TryParseExact(result.Member.ToString(), "N", out var courierId))
                continue;

            var at = await db.HashGetAsync(keys.Courier(courierId), "at");
            if (at.IsNullOrEmpty || !long.TryParse(at.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                continue;

            nearby.Add(new NearbyCourier(courierId, result.Distance ?? 0, DateTimeOffset.FromUnixTimeMilliseconds(ms)));
        }

        return nearby;
    }

    private static CourierPosition? Parse(Guid courierId, HashEntry[] entries)
    {
        if (entries.Length == 0)
            return null;

        var map = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
        return map.TryGetValue("lat", out var lat) && map.TryGetValue("lng", out var lng) && map.TryGetValue("at", out var at)
            ? new CourierPosition(
                courierId,
                double.Parse(lat, CultureInfo.InvariantCulture),
                double.Parse(lng, CultureInfo.InvariantCulture),
                DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(at, CultureInfo.InvariantCulture)))
            : null;
    }
}
