using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Options;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Deletes expired tokens/sessions and audit entries older than the retention period.</summary>
public sealed class RetentionCleanupJob(IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<RetentionCleanupJob> logger)
    : PeriodicJob(scopes, logger)
{
    protected override string Name => "retention-cleanup";

    protected override JobSettings Settings => options.Value.RetentionCleanup;

    protected override async Task<int> RunOnceAsync(IServiceProvider services, int batchSize, CancellationToken cancellationToken)
    {
        var now = services.GetRequiredService<IClock>().UtcNow;
        var auth = services.GetRequiredService<IOptions<AuthOptions>>().Value;
        var audit = services.GetRequiredService<IOptions<AuditOptions>>().Value;

        var tokens = await services.GetRequiredService<IAuthTokenRepository>()
            .DeleteExpiredAsync(now.AddDays(-auth.TokenRetentionDays), batchSize, cancellationToken);
        var audits = await services.GetRequiredService<DatabaseMaintenance>()
            .DeleteAuditOlderThanAsync(now.AddDays(-audit.RetentionDays), cancellationToken);
        return tokens + audits;
    }
}
