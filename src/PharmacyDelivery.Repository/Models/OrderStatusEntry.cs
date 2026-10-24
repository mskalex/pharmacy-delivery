using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Repository.Models;

public sealed record OrderStatusEntry(OrderStatus? From, OrderStatus To, DateTimeOffset At, string? Reason);
