using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Repository.Models;

public sealed record OrderDetails(
    Guid Id,
    string Number,
    Guid CustomerId,
    OrderStatus Status,
    decimal Total,
    string Currency,
    DateTimeOffset CreatedAt,
    DateTimeOffset EstimatedDeliveryAt,
    DateTimeOffset? DeliveredAt,
    AddressInput DeliveryAddress,
    PharmacyInfo Pharmacy,
    IReadOnlyList<OrderItemView> Items,
    IReadOnlyList<OrderStatusEntry> History,
    Guid? CourierId,
    string? CourierName);
