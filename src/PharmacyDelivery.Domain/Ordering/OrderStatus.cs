using System.Collections.Frozen;

namespace PharmacyDelivery.Domain.Ordering;

public enum OrderStatus
{
    AwaitingPayment,
    PaymentFailed,
    Paid,
    AssignedToPharmacy,
    Accepted,
    ReadyForPickup,
    CourierAssigned,
    InDelivery,
    Delivered,
    Cancelled,
    Refunded,
}

/// <summary>Allowed order status transitions (see the state diagram in DESIGN.md §7).</summary>
public static class OrderStateMachine
{
    private static readonly FrozenDictionary<OrderStatus, OrderStatus[]> Transitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.AwaitingPayment] = [OrderStatus.Paid, OrderStatus.PaymentFailed, OrderStatus.Cancelled],
            [OrderStatus.PaymentFailed] = [OrderStatus.Paid, OrderStatus.Cancelled],
            [OrderStatus.Paid] = [OrderStatus.AssignedToPharmacy, OrderStatus.Cancelled],
            // AssignedToPharmacy -> AssignedToPharmacy is a reassignment to the next pharmacy.
            [OrderStatus.AssignedToPharmacy] = [OrderStatus.Accepted, OrderStatus.AssignedToPharmacy, OrderStatus.Cancelled],
            [OrderStatus.Accepted] = [OrderStatus.ReadyForPickup, OrderStatus.Cancelled],
            [OrderStatus.ReadyForPickup] = [OrderStatus.CourierAssigned],
            [OrderStatus.CourierAssigned] = [OrderStatus.InDelivery, OrderStatus.ReadyForPickup],
            [OrderStatus.InDelivery] = [OrderStatus.Delivered],
            [OrderStatus.Delivered] = [],
            [OrderStatus.Cancelled] = [OrderStatus.Refunded],
            [OrderStatus.Refunded] = [],
        }.ToFrozenDictionary();

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from) =>
        Transitions.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>Statuses in which the pharmacy panel shows the order.</summary>
    public static IReadOnlyList<OrderStatus> ActiveInPharmacy { get; } =
        [OrderStatus.AssignedToPharmacy, OrderStatus.Accepted, OrderStatus.ReadyForPickup, OrderStatus.CourierAssigned];

    /// <summary>Statuses in which the customer may still cancel.</summary>
    public static bool IsCancellableByCustomer(OrderStatus status) =>
        status is OrderStatus.AwaitingPayment or OrderStatus.PaymentFailed or OrderStatus.Paid or OrderStatus.AssignedToPharmacy;

    /// <summary>Payment has been captured in these statuses, so cancelling requires a refund.</summary>
    public static bool IsPaid(OrderStatus status) =>
        status is OrderStatus.Paid or OrderStatus.AssignedToPharmacy or OrderStatus.Accepted or OrderStatus.ReadyForPickup
            or OrderStatus.CourierAssigned or OrderStatus.InDelivery or OrderStatus.Delivered;
}
