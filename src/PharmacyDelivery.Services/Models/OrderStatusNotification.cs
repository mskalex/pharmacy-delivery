namespace PharmacyDelivery.Services.Models;

/// <summary>Order status change pushed to subscribers (customer app, pharmacy panel).</summary>
public sealed record OrderStatusNotification(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    Guid PharmacyId,
    string Status,
    DateTimeOffset OccurredAt);
