using PharmacyDelivery.Domain.Abstract;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>
/// Dispatches domain events collected from tracked aggregates. Called by the unit of work right before
/// SaveChanges, so handlers run inside the same transaction (implementation: MediatR, in PharmacyDelivery.Services).
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Publishes the events to their in-process handlers inside the current transaction.</summary>
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken);
}
