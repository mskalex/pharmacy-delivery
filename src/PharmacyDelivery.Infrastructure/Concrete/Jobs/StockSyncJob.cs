using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Synchronizes stock with pharmacy systems in parallel (<see cref="Parallel.ForEachAsync{TSource}(IEnumerable{TSource}, ParallelOptions, Func{TSource, CancellationToken, ValueTask})"/>).
/// Each pharmacy gets its own DI scope: DbContext is not thread-safe and must never be shared between tasks.
/// </summary>
public sealed class StockSyncJob(IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<StockSyncJob> logger)
    : PeriodicJob(scopes, logger)
{
    protected override string Name => "stock-sync";

    protected override JobSettings Settings => options.Value.StockSync;

    protected override async Task<int> RunOnceAsync(IServiceProvider services, int batchSize, CancellationToken cancellationToken)
    {
        var pharmacyIds = await services.GetRequiredService<IStockSyncService>().GetPharmacyIdsAsync(cancellationToken);
        var changed = 0;
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = batchSize, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(pharmacyIds, parallel, async (pharmacyId, ct) =>
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            try
            {
                var count = await scope.ServiceProvider.GetRequiredService<IStockSyncService>().SyncPharmacyAsync(pharmacyId, ct);
                Interlocked.Add(ref changed, count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Stock sync of pharmacy {PharmacyId} failed", pharmacyId);
            }
        });
        return changed;
    }
}
