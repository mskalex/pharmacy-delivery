using PharmacyDelivery.Domain.Deliveries;

namespace PharmacyDelivery.Repository.Models;

public sealed record CurrentDeliveryInfo(
    Guid DeliveryId,
    Guid OrderId,
    string OrderNumber,
    DeliveryStatus Status,
    PharmacyInfo Pharmacy,
    AddressInput Destination);
