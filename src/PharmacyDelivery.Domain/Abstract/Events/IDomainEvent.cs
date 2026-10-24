namespace PharmacyDelivery.Domain.Abstract;

/// <summary>Marker for facts that happened inside an aggregate. Mapped to integration events by the infrastructure.</summary>
public interface IDomainEvent
{
    /// <summary>Moment the fact happened (UTC).</summary>
    DateTimeOffset OccurredAt { get; }
}
