using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmacyDelivery.Infrastructure.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Base of periodic background jobs. Every tick runs in a fresh DI scope (own DbContext). Jobs are safe to run on
/// several instances at once: the work they pick is claimed with <c>FOR UPDATE SKIP LOCKED</c>.
/// </summary>
public abstract class PeriodicJob(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    protected IServiceScopeFactory ScopeFactory { get; } = scopeFactory;

    protected abstract string Name { get; }

    protected abstract JobSettings Settings { get; }

    protected abstract Task<int> RunOnceAsync(IServiceProvider services, int batchSize, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Settings.Enabled)
        {
            logger.LogInformation("Job {Job} is disabled", Name);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Settings.IntervalSeconds));
        do
        {
            try
            {
                await using var scope = ScopeFactory.CreateAsyncScope();
                var processed = await RunOnceAsync(scope.ServiceProvider, Settings.BatchSize, stoppingToken);
                if (processed > 0)
                    logger.LogInformation("Job {Job} processed {Count} items", Name, processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failing tick must not kill the loop; the next tick retries.
                logger.LogError(ex, "Job {Job} failed", Name);
            }
        } while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
