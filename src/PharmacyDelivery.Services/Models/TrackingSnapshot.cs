using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Models;

public sealed record TrackingSnapshot(
    Guid OrderId,
    OrderStatus Status,
    PharmacyInfo Pharmacy,
    AddressInput Destination,
    CourierPosition? Courier,
    DateTimeOffset EstimatedDeliveryAt);
