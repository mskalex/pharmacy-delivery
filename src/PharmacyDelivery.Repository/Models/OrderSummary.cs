using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Repository.Models;

public sealed record OrderSummary(
    Guid Id,
    string Number,
    OrderStatus Status,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAt,
    string PharmacyName);
