using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Messaging.Contracts.V1;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>Maps domain events to versioned integration contracts (published through the outbox).</summary>
public static class IntegrationEventMapper
{
    public static IEnumerable<object> Map(IDomainEvent domainEvent)
    {
        if (domainEvent is not OrderStatusChangedDomainEvent changed)
            yield break;

        var orderId = changed.OrderId.Value;
        var pharmacyId = changed.PharmacyId.Value;

        yield return new OrderStatusChanged(
            orderId,
            changed.OrderNumber,
            changed.CustomerId.Value,
            pharmacyId,
            changed.PreviousStatus?.ToString(),
            changed.NewStatus.ToString(),
            changed.OccurredAt);

        switch (changed.NewStatus)
        {
            case OrderStatus.Paid:
                yield return new OrderPaid(orderId, pharmacyId, changed.OccurredAt);
                break;
            case OrderStatus.Accepted:
                yield return new OrderAcceptedByPharmacy(orderId, pharmacyId, changed.OccurredAt);
                break;
            case OrderStatus.ReadyForPickup:
                yield return new OrderReadyForPickup(orderId, pharmacyId, changed.OccurredAt);
                break;
            case OrderStatus.Delivered:
                yield return new DeliveryCompleted(orderId, null, changed.OccurredAt);
                break;
        }
    }
}
