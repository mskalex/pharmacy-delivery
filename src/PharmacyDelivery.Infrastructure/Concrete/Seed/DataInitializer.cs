using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Messaging;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Repository.Options;
using PharmacyDelivery.Services.Abstract;
using StackExchange.Redis;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Startup data initialization and maintenance reset:
/// <list type="number">
/// <item>cluster-wide advisory lock (only one instance migrates/seeds at a time);</item>
/// <item>migrations (<c>Database:ApplyMigrationsOnStartup</c>);</item>
/// <item><c>Seed:Mode</c>: <c>None</c> | <c>IfEmpty</c> | <c>Reset</c> (truncate all tables, delete <c>pharmacy-delivery:*</c> Redis keys,
/// optionally purge queues, then seed). Reset requires <c>Seed:AllowReset</c>.</item>
/// </list>
/// </summary>
public sealed class DataInitializer(
    DatabaseMaintenance database,
    SeederRegistry registry,
    IServiceProvider services,
    IConnectionMultiplexer redis,
    RedisKeys redisKeys,
    IHttpClientFactory httpClientFactory,
    IOptions<SeedOptions> seedOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<RabbitMqOptions> rabbitOptions,
    ILogger<DataInitializer> logger) : IDataMaintenance
{
    public const string RabbitManagementClient = "rabbitmq-management";

    /// <summary>A reset done by another instance within this window is not repeated (e.g. docker compose --scale api=3).</summary>
    private static readonly TimeSpan ResetDeduplicationWindow = TimeSpan.FromMinutes(5);

    private readonly SeedOptions _seed = seedOptions.Value;

    public IReadOnlyCollection<string> AvailableProfiles => registry.Profiles;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var startupLock = await database.AcquireStartupLockAsync(cancellationToken);

        if (databaseOptions.Value.ApplyMigrationsOnStartup)
            await database.MigrateAsync(cancellationToken);

        switch (_seed.Mode)
        {
            case "IfEmpty" when !await database.HasDataAsync(cancellationToken):
                await SeedAsync(_seed.Profile, cancellationToken);
                break;
            case "IfEmpty":
                logger.LogInformation("Seed skipped: database already contains data");
                break;
            case "Reset":
                EnsureResetAllowed();
                if (await redis.GetDatabase().KeyExistsAsync(redisKeys.SeedResetMarker))
                {
                    logger.LogInformation("Seed reset skipped: another instance reset the data moments ago");
                    break;
                }

                await ResetCoreAsync(_seed.Profile, cancellationToken);
                break;
        }
    }

    public async Task ResetAsync(string? profile, CancellationToken cancellationToken)
    {
        EnsureResetAllowed();
        await using var startupLock = await database.AcquireStartupLockAsync(cancellationToken);
        await ResetCoreAsync(string.IsNullOrWhiteSpace(profile) ? _seed.Profile : profile, cancellationToken);
    }

    private void EnsureResetAllowed()
    {
        if (!_seed.AllowReset)
            throw new InvalidOperationException("Seed reset is not allowed in this environment (Seed:AllowReset=false).");
    }

    private async Task ResetCoreAsync(string profile, CancellationToken cancellationToken)
    {
        // Validate the profile before deleting anything.
        registry.GetSeeders(profile);

        logger.LogWarning("RESET: deleting all application data and reseeding with profile {Profile}", profile);
        var tables = await database.TruncateAllAsync(cancellationToken);
        var keys = await DeleteRedisKeysAsync();
        if (_seed.PurgeQueuesOnReset)
            await PurgeQueuesAsync(cancellationToken);

        await SeedAsync(profile, cancellationToken);
        await redis.GetDatabase().StringSetAsync(redisKeys.SeedResetMarker, DateTimeOffset.UtcNow.ToString("O"), ResetDeduplicationWindow);
        logger.LogWarning("RESET done: {Tables} tables truncated, {Keys} Redis keys deleted, profile {Profile} seeded", tables.Count, keys,
            profile);
    }

    private async Task SeedAsync(string profile, CancellationToken cancellationToken)
    {
        foreach (var seederType in registry.GetSeeders(profile))
        {
            await using var scope = services.CreateAsyncScope();
            var seeder = (ISeeder)ActivatorUtilities.CreateInstance(scope.ServiceProvider, seederType);
            logger.LogInformation("Seeding {Seeder} ({Profile})", seederType.Name, profile);
            await seeder.SeedAsync(cancellationToken);
        }
    }

    private async Task<int> DeleteRedisKeysAsync()
    {
        var deleted = 0;
        foreach (var endpoint in redis.GetEndPoints())
        {
            var server = redis.GetServer(endpoint);
            if (server.IsReplica)
                continue;

            var batch = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: redisKeys.Prefix + "*", pageSize: 500))
            {
                batch.Add(key);
                if (batch.Count == 500)
                {
                    deleted += (int)await redis.GetDatabase().KeyDeleteAsync([.. batch]);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                deleted += (int)await redis.GetDatabase().KeyDeleteAsync([.. batch]);
        }

        return deleted;
    }

    private async Task PurgeQueuesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(RabbitManagementClient);
            var vhost = Uri.EscapeDataString(rabbitOptions.Value.VirtualHost);
            var queues = await client.GetFromJsonAsync<List<QueueInfo>>($"/api/queues/{vhost}", cancellationToken) ?? [];
            foreach (var queue in queues.Where(q => q.Name.StartsWith(MessagingDefaults.EndpointPrefix, StringComparison.Ordinal)))
            {
                using var response = await client.DeleteAsync($"/api/queues/{vhost}/{Uri.EscapeDataString(queue.Name)}/contents",
                    cancellationToken);
                logger.LogWarning("Queue {Queue} purged: {Status}", queue.Name, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not purge RabbitMQ queues (management API unavailable)");
        }
    }

    private sealed record QueueInfo(string Name);
}
