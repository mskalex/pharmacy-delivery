namespace PharmacyDelivery.Services.Abstract;

/// <summary>Untyped view of a domain event notification (used for tracing).</summary>
internal interface IDomainEventNotification
{
    /// <summary>Name of the wrapped domain event type.</summary>
    string EventName { get; }
}
