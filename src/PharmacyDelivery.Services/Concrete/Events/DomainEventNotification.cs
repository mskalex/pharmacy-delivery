using MediatR;
using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// MediatR notification wrapping a domain event. The domain model stays free of MediatR: aggregates raise plain
/// <see cref="IDomainEvent"/> records, the wrapper is created here.
/// </summary>
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification, IDomainEventNotification
    where TEvent : IDomainEvent
{
    public string EventName => typeof(TEvent).Name;
}
