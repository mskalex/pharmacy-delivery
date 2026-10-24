using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Reassigns orders that the pharmacy did not accept in time.</summary>
public sealed class AcceptanceTimeoutJob(IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<AcceptanceTimeoutJob> logger)
    : PeriodicJob(scopes, logger)
{
    protected override string Name => "acceptance-timeout";

    protected override JobSettings Settings => options.Value.AcceptanceTimeout;

    protected override Task<int> RunOnceAsync(IServiceProvider services, int batchSize, CancellationToken cancellationToken) =>
        services.GetRequiredService<IPharmacyOrderService>().ProcessAcceptanceTimeoutsAsync(batchSize, cancellationToken);
}
