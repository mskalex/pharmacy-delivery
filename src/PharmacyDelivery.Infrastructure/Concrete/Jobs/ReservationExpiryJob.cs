using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Releases reservations of unpaid orders after the TTL and cancels those orders.</summary>
public sealed class ReservationExpiryJob(IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<ReservationExpiryJob> logger)
    : PeriodicJob(scopes, logger)
{
    protected override string Name => "reservation-expiry";

    protected override JobSettings Settings => options.Value.ReservationExpiry;

    protected override Task<int> RunOnceAsync(IServiceProvider services, int batchSize, CancellationToken cancellationToken) =>
        services.GetRequiredService<IStockReservationService>().ExpireAsync(batchSize, cancellationToken);
}
