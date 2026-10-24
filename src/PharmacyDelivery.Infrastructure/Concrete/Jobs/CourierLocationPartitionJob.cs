using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Maintains daily partitions of <c>courier_locations</c>: creates them ahead of time and drops partitions older
/// than the retention period (DROP is instant and leaves no bloat, unlike DELETE).
/// </summary>
public sealed class CourierLocationPartitionJob(
    IServiceScopeFactory scopes,
    IOptions<JobsOptions> options,
    ILogger<CourierLocationPartitionJob> logger)
    : PeriodicJob(scopes, logger)
{
    private const string Prefix = "courier_locations_p";

    protected override string Name => "partition-maintenance";

    protected override JobSettings Settings => options.Value.PartitionMaintenance;

    protected override async Task<int> RunOnceAsync(IServiceProvider services, int batchSize, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<PharmacyDeliveryDbContext>();
        var tracking = services.GetRequiredService<IOptions<TrackingOptions>>().Value;
        var today = DateOnly.FromDateTime(services.GetRequiredService<IClock>().UtcNow.UtcDateTime);
        var changes = 0;

        for (var day = today.AddDays(-1); day <= today.AddDays(tracking.PartitionsAheadDays); day = day.AddDays(1))
        {
            var name = Prefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var from = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var sql =
                $"CREATE TABLE IF NOT EXISTS {name} PARTITION OF courier_locations FOR VALUES FROM ('{from} 00:00:00+00') TO ('{to} 00:00:00+00')";
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Happens if rows for that day already landed in the default partition.
                logger.LogWarning(ex, "Could not create partition {Partition}", name);
            }
        }

        var cutoff = today.AddDays(-tracking.HistoryRetentionDays);
        var partitions = await db.Database
            .SqlQueryRaw<string>(
                "SELECT c.relname AS \"Value\" FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid "
                + "JOIN pg_class p ON p.oid = i.inhparent WHERE p.relname = 'courier_locations'")
            .ToListAsync(cancellationToken);

        foreach (var partition in partitions.Where(p => p.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            if (DateOnly.TryParseExact(partition[Prefix.Length..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var day)
                && day < cutoff)
            {
                // The name is rebuilt from the parsed date, so nothing from the catalog is concatenated into SQL.
                var drop = "DROP TABLE IF EXISTS " + Prefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                await db.Database.ExecuteSqlRawAsync(drop, cancellationToken);
                logger.LogInformation("Dropped expired partition {Partition}", partition);
                changes++;
            }
        }

        return changes;
    }
}
