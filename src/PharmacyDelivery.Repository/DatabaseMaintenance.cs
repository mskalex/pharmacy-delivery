using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace PharmacyDelivery.Repository;

/// <summary>
/// Schema and data maintenance: migrations, full reset and the cluster-wide startup lock
/// (<c>pg_advisory_lock</c>) that makes "migrate + seed" run on exactly one instance at a time.
/// </summary>
public sealed class DatabaseMaintenance(PharmacyDeliveryDbContext db, ILogger<DatabaseMaintenance> logger)
{
    /// <summary>Arbitrary constant identifying the "PharmacyDelivery startup" advisory lock.</summary>
    private const long StartupLockKey = 0x4150_5445_4B41_0001;

    /// <summary>Blocks until no other instance is migrating/seeding; dispose to release.</summary>
    public async Task<IAsyncDisposable> AcquireStartupLockAsync(CancellationToken cancellationToken)
    {
        // Clone keeps the credentials (ConnectionString of an opened connection no longer contains the password).
        var connection = (NpgsqlConnection)((ICloneable)db.Database.GetDbConnection()).Clone();
        await connection.OpenAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
        {
            command.Parameters.AddWithValue("key", StartupLockKey);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation("Startup advisory lock acquired");
        return new AdvisoryLock(connection);
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
            return;

        logger.LogInformation("Applying {Count} migrations: {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task<bool> HasDataAsync(CancellationToken cancellationToken) => db.Users.AsNoTracking().AnyAsync(cancellationToken);

    /// <summary>Deletes all application data (TRUNCATE … RESTART IDENTITY CASCADE). The schema is kept.</summary>
    public async Task<IReadOnlyList<string>> TruncateAllAsync(CancellationToken cancellationToken)
    {
        var tables = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned())
            .Select(t => t.GetTableName())
            .OfType<string>()
            .Distinct()
            .Order()
            .ToList();

        var sql = "TRUNCATE TABLE " + string.Join(", ", tables.Select(t => "\"" + t + "\"")) + " RESTART IDENTITY CASCADE";

        // TRUNCATE needs ACCESS EXCLUSIVE locks on every table; background consumers may hold row locks at that moment.
        // Bounded waiting + retry on deadlock instead of blocking the whole database behind the TRUNCATE.
        for (var attempt = 1;; attempt++)
        {
            try
            {
                // User transactions must run inside the retry execution strategy (it also retries 40P01 itself).
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
                {
                    await using var transaction = await db.Database.BeginTransactionAsync(ct);
                    await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
                    await transaction.CommitAsync(ct);
                }, cancellationToken);
                break;
            }
            catch (Exception ex) when (attempt < 5 && FindPostgres(ex)?.SqlState is "40P01" or "55P03")
            {
                logger.LogWarning("TRUNCATE attempt {Attempt} failed with {SqlState}; retrying", attempt, FindPostgres(ex)!.SqlState);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
            }
        }

        logger.LogWarning("All application tables truncated: {Tables}", string.Join(", ", tables));
        return tables;
    }

    /// <summary>Audit retention: deletes audit entries (and their changes, by cascade) older than the cutoff.</summary>
    public Task<int> DeleteAuditOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        db.AuditEntries.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);

    private static PostgresException? FindPostgres(Exception? exception)
    {
        while (exception is not null and not PostgresException)
            exception = exception.InnerException;

        return exception as PostgresException;
    }

    private sealed class AdvisoryLock(NpgsqlConnection connection) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", StartupLockKey);
                await command.ExecuteNonQueryAsync();
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
