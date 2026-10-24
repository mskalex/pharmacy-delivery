using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Assigns couriers to orders ready for pickup.</summary>
public interface IDispatchService
{
    /// <summary>Consumer of <c>OrderReadyForPickup</c>: assigns the nearest available courier.</summary>
    Task<DispatchOutcome> DispatchAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Retries dispatch for ready orders still waiting for a courier.</summary>
    Task<int> RetryPendingAsync(int batchSize, CancellationToken cancellationToken);
}
