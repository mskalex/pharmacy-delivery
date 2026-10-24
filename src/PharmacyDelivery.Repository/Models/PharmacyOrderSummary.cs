using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Repository.Models;

public sealed record PharmacyOrderSummary(
    Guid Id,
    string Number,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AssignedAt,
    DateTimeOffset? AcceptDeadline,
    IReadOnlyList<OrderItemView> Items);
