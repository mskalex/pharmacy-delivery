using PharmacyDelivery.Services.Abstract;
using StackExchange.Redis;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Single-instance Redis lock: <c>SET key token NX PX ttl</c>; release deletes the key only if it still holds our
/// token (Lua), so an expired lock taken over by someone else is never released by mistake.
/// </summary>
public sealed class RedisDistributedLock(IConnectionMultiplexer redis, RedisKeys keys) : IDistributedLock
{
    private const string ReleaseScript = """
                                         if redis.call('get', KEYS[1]) == ARGV[1] then
                                             return redis.call('del', KEYS[1])
                                         else
                                             return 0
                                         end
                                         """;

    public async Task<IAsyncDisposable?> TryAcquireAsync(string resource, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var key = keys.Lock(resource);
        var token = Guid.NewGuid().ToString("N");
        return await db.StringSetAsync(key, token, ttl, When.NotExists)
            ? new Handle(db, key, token)
            : null;
    }

    private sealed class Handle(IDatabase db, RedisKey key, string token) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() =>
            await db.ScriptEvaluateAsync(ReleaseScript, [key], [token]);
    }
}
