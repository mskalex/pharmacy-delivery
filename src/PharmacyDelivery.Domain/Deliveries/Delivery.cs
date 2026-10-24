using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Deliveries;

public enum DeliveryStatus
{
    Assigned,
    InDelivery,
    Delivered,
    Cancelled,
}

/// <summary>
/// Courier's job for one order. "One active delivery per courier" is enforced twice: by a Redis lock during
/// assignment and by a unique partial index in the database (the database is the last line of defence).
/// </summary>
public sealed class Delivery : AggregateRoot<DeliveryId>
{
    private Delivery()
    {
    }

    public OrderId OrderId { get; private set; }

    public CourierId CourierId { get; private set; }

    public PharmacyId PharmacyId { get; private set; }

    public DeliveryStatus Status { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public DateTimeOffset? PickedUpAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public bool IsActive => Status is DeliveryStatus.Assigned or DeliveryStatus.InDelivery;

    public static Delivery Assign(DeliveryId id, OrderId orderId, CourierId courierId, PharmacyId pharmacyId, DateTimeOffset now) =>
        new()
        {
            Id = id,
            OrderId = orderId,
            CourierId = courierId,
            PharmacyId = pharmacyId,
            Status = DeliveryStatus.Assigned,
            AssignedAt = now,
        };

    public void PickUp(DateTimeOffset now)
    {
        Ensure(DeliveryStatus.Assigned, DeliveryStatus.InDelivery);
        Status = DeliveryStatus.InDelivery;
        PickedUpAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        Ensure(DeliveryStatus.InDelivery, DeliveryStatus.Delivered);
        Status = DeliveryStatus.Delivered;
        DeliveredAt = now;
    }

    public void Cancel()
    {
        Ensure(DeliveryStatus.Assigned, DeliveryStatus.Cancelled);
        Status = DeliveryStatus.Cancelled;
    }

    private void Ensure(DeliveryStatus expected, DeliveryStatus target)
    {
        if (Status != expected)
        {
            throw new DomainException(
                ErrorCodes.DeliveryInvalidTransition,
                $"Delivery {Id} cannot move from {Status} to {target}.");
        }
    }
}
