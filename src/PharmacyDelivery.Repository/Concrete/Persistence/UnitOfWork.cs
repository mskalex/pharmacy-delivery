using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Options;

namespace PharmacyDelivery.Repository.Concrete;

/// <summary>
/// Transaction boundary:
/// <list type="bullet">
/// <item>domain events of tracked aggregates are dispatched (MediatR) before saving; handlers publish integration
/// events to the MassTransit transactional outbox, so they commit together with the business change;</item>
/// <item>transactions run under the Npgsql retry execution strategy; the whole operation is re-executed on
/// transient errors (deadlock, serialization failure, connection loss);</item>
/// <item><c>lock_timeout</c> bounds waiting for row locks;</item>
/// <item>database exceptions leave as application exceptions (<see cref="PostgresErrorTranslator"/>).</item>
/// </list>
/// Inside a MassTransit consumer the outbox already opened a transaction; it is reused (ambient transaction).
/// </summary>
public sealed class UnitOfWork(
    PharmacyDeliveryDbContext db,
    IDomainEventDispatcher domainEvents,
    PostgresErrorTranslator translator,
    IOptions<DatabaseOptions> options) : IUnitOfWork
{
    private const int MaxDispatchRounds = 5;

    private readonly string _lockTimeoutSql = "SET LOCAL lock_timeout = " + options.Value.LockTimeoutMs;
    private bool _insideStrategy;

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (_insideStrategy)
        {
            // Raw exceptions let the execution strategy decide whether to retry; translation happens outside.
            await SaveCoreAsync(cancellationToken);
            return;
        }

        try
        {
            await SaveCoreAsync(cancellationToken);
        }
        catch (Exception ex) when (translator.Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
            return await RunAmbientAsync(operation, cancellationToken);

        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            _insideStrategy = true;
            return await strategy.ExecuteAsync(
                operation,
                async (_, op, ct) =>
                {
                    // A retry starts from a clean state: entities of the failed attempt are discarded.
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                    await db.Database.ExecuteSqlRawAsync(_lockTimeoutSql, ct);
                    var result = await op(ct);
                    await transaction.CommitAsync(ct);
                    return result;
                },
                verifySucceeded: null,
                cancellationToken);
        }
        catch (Exception ex) when (translator.Translate(ex) is { } translated)
        {
            throw translated;
        }
        finally
        {
            _insideStrategy = false;
        }
    }

    private async Task<T> RunAmbientAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(_lockTimeoutSql, cancellationToken);
            return await operation(cancellationToken);
        }
        catch (Exception ex) when (translator.Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        // Handlers may change aggregates and raise further events: repeat until quiet (bounded to avoid cycles).
        for (var round = 0; round < MaxDispatchRounds; round++)
        {
            var aggregates = db.ChangeTracker.Entries<IAggregateRoot>()
                .Select(e => e.Entity)
                .Where(a => a.DomainEvents.Count > 0)
                .ToList();
            if (aggregates.Count == 0)
                break;

            var pending = aggregates.SelectMany(a => a.DomainEvents).ToList();
            foreach (var aggregate in aggregates)
                aggregate.ClearDomainEvents();

            await domainEvents.DispatchAsync(pending, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
