using MediatR;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Translates the domain event into versioned integration events and publishes them to the transactional outbox
/// (they leave the process only if the business transaction commits).
/// </summary>
public sealed class PublishOrderIntegrationEventsHandler(IIntegrationEventPublisher publisher)
    : INotificationHandler<DomainEventNotification<OrderStatusChangedDomainEvent>>
{
    public async Task Handle(DomainEventNotification<OrderStatusChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        foreach (var message in IntegrationEventMapper.Map(notification.DomainEvent))
            await publisher.PublishAsync(message, cancellationToken);
    }
}
