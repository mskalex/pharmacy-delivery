namespace PharmacyDelivery.Messaging.Contracts.V1;

// Integration events (version 1). Rules — see docs/ai/messaging.md:
//  * only additive changes (new nullable/optional members); consumers ignore unknown fields;
//  * a breaking change means a new contract in Contracts.V2, both are published during migration;
//  * no domain types: primitives and Guid only.

/// <summary>Any status change of an order. Consumed to push updates to the customer (SignalR) and the pharmacy panel.</summary>
public sealed record OrderStatusChanged(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid PharmacyId,
    string? PreviousStatus,
    string NewStatus,
    DateTimeOffset OccurredAt);

/// <summary>Payment captured. Consumed to hand the order to the selected pharmacy.</summary>
public sealed record OrderPaid(Guid OrderId, Guid PharmacyId, DateTimeOffset PaidAt);

/// <summary>The pharmacy confirmed it will prepare the order.</summary>
public sealed record OrderAcceptedByPharmacy(Guid OrderId, Guid PharmacyId, DateTimeOffset AcceptedAt);

/// <summary>Order packed and waiting for a courier. Consumed by dispatch.</summary>
public sealed record OrderReadyForPickup(Guid OrderId, Guid PharmacyId, DateTimeOffset ReadyAt);

/// <summary>Order delivered to the customer. Consumed to free the courier.</summary>
public sealed record DeliveryCompleted(Guid OrderId, Guid? CourierId, DateTimeOffset DeliveredAt);
