using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Models;

public sealed record CheckoutResult(
    Guid OrderId,
    string OrderNumber,
    OrderStatus Status,
    decimal Total,
    string Currency,
    PharmacyInfo Pharmacy,
    DateTimeOffset EstimatedDeliveryAt,
    DateTimeOffset ReservationExpiresAt,
    bool IsReplay);
