using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Domain.Ordering;

/// <summary>Line requested at checkout; price and name are snapshotted into <see cref="OrderItem"/>.</summary>
public sealed record OrderLine(ProductId ProductId, string ProductName, Money UnitPrice, int Quantity);

/// <summary>
/// Customer order. All status changes go through <see cref="TransitionTo"/>, which validates the state machine,
/// appends the status history and raises <see cref="OrderStatusChangedDomainEvent"/>.
/// </summary>
public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusChange> _history = [];

    private Order()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public UserId CustomerId { get; private set; }

    public PharmacyId PharmacyId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DeliveryAddress DeliveryAddress { get; private set; } = null!;

    public Money Total { get; private set; } = Money.Zero;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>When the order was (last) handed to a pharmacy; drives the acceptance timeout.</summary>
    public DateTimeOffset? AssignedAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset EstimatedDeliveryAt { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Pharmacies that rejected or timed out on this order; they are skipped on reassignment.</summary>
    public List<Guid> RejectedPharmacyIds { get; private set; } = [];

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    public IReadOnlyList<OrderStatusChange> History => _history.AsReadOnly();

    public static Order Create(
        OrderId id,
        string number,
        UserId customerId,
        PharmacyId pharmacyId,
        DeliveryAddress address,
        IReadOnlyCollection<OrderLine> lines,
        string idempotencyKey,
        DateTimeOffset now,
        DateTimeOffset estimatedDeliveryAt)
    {
        if (lines.Count == 0)
            throw new DomainException(ErrorCodes.CartEmpty, "An order must contain at least one item.");

        var order = new Order
        {
            Id = id,
            Number = Guard.NotBlank(number),
            CustomerId = customerId,
            PharmacyId = pharmacyId,
            Status = OrderStatus.AwaitingPayment,
            DeliveryAddress = address,
            IdempotencyKey = Guard.NotBlank(idempotencyKey),
            CreatedAt = now,
            UpdatedAt = now,
            EstimatedDeliveryAt = estimatedDeliveryAt,
        };

        foreach (var line in lines)
        {
            order._items.Add(OrderItem.Create(id, line));
        }

        order.Total = order._items.Aggregate(Money.Zero, (sum, item) => sum.Add(item.LineTotal));
        order._history.Add(OrderStatusChange.Create(id, null, OrderStatus.AwaitingPayment, now, null));
        order.Raise(new OrderStatusChangedDomainEvent(id, order.Number, customerId, pharmacyId, null, OrderStatus.AwaitingPayment, now));
        return order;
    }

    public void MarkPaid(DateTimeOffset now)
    {
        PaidAt = now;
        TransitionTo(OrderStatus.Paid, now);
    }

    public void MarkPaymentFailed(DateTimeOffset now) => TransitionTo(OrderStatus.PaymentFailed, now);

    public void AssignToPharmacy(DateTimeOffset now)
    {
        AssignedAt = now;
        TransitionTo(OrderStatus.AssignedToPharmacy, now);
    }

    /// <summary>The current pharmacy rejected or timed out; the order moves to <paramref name="nextPharmacy"/>.</summary>
    public void Reassign(PharmacyId nextPharmacy, DateTimeOffset now, DateTimeOffset estimatedDeliveryAt, string reason)
    {
        if (nextPharmacy == PharmacyId)
            throw new DomainException(ErrorCodes.OrderInvalidTransition, "Cannot reassign an order to the same pharmacy.");

        if (!RejectedPharmacyIds.Contains(PharmacyId.Value))
            RejectedPharmacyIds.Add(PharmacyId.Value);

        PharmacyId = nextPharmacy;
        AssignedAt = now;
        EstimatedDeliveryAt = estimatedDeliveryAt;
        TransitionTo(OrderStatus.AssignedToPharmacy, now, reason);
    }

    public void MarkRejectedBy(PharmacyId pharmacyId)
    {
        if (!RejectedPharmacyIds.Contains(pharmacyId.Value))
            RejectedPharmacyIds.Add(pharmacyId.Value);
    }

    public void Accept(DateTimeOffset now) => TransitionTo(OrderStatus.Accepted, now);

    public void MarkReadyForPickup(DateTimeOffset now) => TransitionTo(OrderStatus.ReadyForPickup, now);

    public void MarkCourierAssigned(DateTimeOffset now) => TransitionTo(OrderStatus.CourierAssigned, now);

    public void MarkCourierUnassigned(DateTimeOffset now, string reason) => TransitionTo(OrderStatus.ReadyForPickup, now, reason);

    public void MarkInDelivery(DateTimeOffset now) => TransitionTo(OrderStatus.InDelivery, now);

    public void MarkDelivered(DateTimeOffset now)
    {
        DeliveredAt = now;
        TransitionTo(OrderStatus.Delivered, now);
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        CancellationReason = Guard.NotBlank(reason);
        TransitionTo(OrderStatus.Cancelled, now, reason);
    }

    public void MarkRefunded(DateTimeOffset now) => TransitionTo(OrderStatus.Refunded, now);

    public void UpdateEstimate(DateTimeOffset estimatedDeliveryAt) => EstimatedDeliveryAt = estimatedDeliveryAt;

    public bool WasPaid => PaidAt is not null;

    private void TransitionTo(OrderStatus target, DateTimeOffset now, string? reason = null)
    {
        if (!OrderStateMachine.CanTransition(Status, target))
        {
            throw new DomainException(
                ErrorCodes.OrderInvalidTransition,
                $"Order {Number} cannot move from {Status} to {target}.");
        }

        var previous = Status;
        Status = target;
        UpdatedAt = now;
        _history.Add(OrderStatusChange.Create(Id, previous, target, now, reason));
        Raise(new OrderStatusChangedDomainEvent(Id, Number, CustomerId, PharmacyId, previous, target, now));
    }
}

public sealed class OrderItem : Entity<Guid>
{
    private OrderItem()
    {
    }

    public OrderId OrderId { get; private set; }

    public ProductId ProductId { get; private set; }

    public string ProductName { get; private set; } = string.Empty;

    public Money UnitPrice { get; private set; } = Money.Zero;

    public int Quantity { get; private set; }

    public Money LineTotal => UnitPrice.Multiply(Quantity);

    internal static OrderItem Create(OrderId orderId, OrderLine line) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            ProductId = line.ProductId,
            ProductName = Guard.NotBlank(line.ProductName),
            UnitPrice = line.UnitPrice,
            Quantity = Guard.Positive(line.Quantity),
        };
}

public sealed class OrderStatusChange : Entity<Guid>
{
    private OrderStatusChange()
    {
    }

    public OrderId OrderId { get; private set; }

    public OrderStatus? FromStatus { get; private set; }

    public OrderStatus ToStatus { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string? Reason { get; private set; }

    internal static OrderStatusChange Create(OrderId orderId, OrderStatus? from, OrderStatus to, DateTimeOffset at, string? reason) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            FromStatus = from,
            ToStatus = to,
            OccurredAt = at,
            Reason = reason,
        };
}

public sealed record OrderStatusChangedDomainEvent(
    OrderId OrderId,
    string OrderNumber,
    UserId CustomerId,
    PharmacyId PharmacyId,
    OrderStatus? PreviousStatus,
    OrderStatus NewStatus,
    DateTimeOffset OccurredAt) : IDomainEvent;
