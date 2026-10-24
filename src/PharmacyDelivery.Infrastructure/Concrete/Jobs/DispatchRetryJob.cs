using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Retries dispatch of ready orders that found no courier the first time.</summary>
public sealed class DispatchRetryJob(IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<DispatchRetryJob> logger)
    : PeriodicJob(scopes, logger)
{
    protected override string Name => "dispatch-retry";

    protected override JobSettings Settings => options.Value.DispatchRetry;

    protected override Task<int> RunOnceAsync(IServiceProvider services, int batchSize, CancellationToken cancellationToken) =>
        services.GetRequiredService<IDispatchService>().RetryPendingAsync(batchSize, cancellationToken);
}
