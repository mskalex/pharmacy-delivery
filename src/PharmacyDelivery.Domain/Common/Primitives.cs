using PharmacyDelivery.Domain.Abstract;

namespace PharmacyDelivery.Domain.Common;

public abstract class Entity<TId>
    where TId : notnull
{
    public TId Id { get; protected init; } = default!;
}

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}

/// <summary>Violation of a business invariant. Carries a stable error code from <c>ErrorCodes</c>.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
