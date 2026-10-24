namespace PharmacyDelivery.Domain.Abstract;

/// <summary>Aggregate root contract used by the unit of work to collect domain events.</summary>
public interface IAggregateRoot
{
    /// <summary>Domain events raised since the aggregate was loaded or last saved.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Removes collected events after they were dispatched (called by the unit of work).</summary>
    void ClearDomainEvents();
}
