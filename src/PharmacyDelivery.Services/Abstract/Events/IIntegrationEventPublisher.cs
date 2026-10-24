namespace PharmacyDelivery.Services.Abstract;

/// <summary>Publishes an integration event to the transactional outbox of the current unit of work.</summary>
public interface IIntegrationEventPublisher
{
    /// <summary>Publishes the message to the outbox of the current transaction.</summary>
    Task PublishAsync(object message, CancellationToken cancellationToken);
}
