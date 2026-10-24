using System.Collections.Concurrent;
using MediatR;
using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>Publishes domain events as MediatR notifications (in-process, inside the current transaction).</summary>
public sealed class MediatRDomainEventDispatcher(IPublisher publisher) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, Func<IDomainEvent, INotification>> Factories = new();

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
            await publisher.Publish(Wrap(domainEvent), cancellationToken);
    }

    private static INotification Wrap(IDomainEvent domainEvent) =>
        Factories.GetOrAdd(domainEvent.GetType(), static type =>
        {
            var notificationType = typeof(DomainEventNotification<>).MakeGenericType(type);
            return e => (INotification)Activator.CreateInstance(notificationType, e)!;
        })(domainEvent);
}
