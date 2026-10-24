using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Repository.Models;

/// <summary>Who may see an order: used by authorization checks without loading the aggregate.</summary>
public sealed record OrderAccessInfo(Guid OrderId, Guid CustomerId, Guid PharmacyId, OrderStatus Status, Guid? CourierId);
