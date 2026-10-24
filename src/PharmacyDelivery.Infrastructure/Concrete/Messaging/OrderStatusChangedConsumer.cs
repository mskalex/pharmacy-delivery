using MassTransit;
using PharmacyDelivery.Messaging.Contracts.V1;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Any status change → real-time push to the customer and the pharmacy panel.</summary>
public sealed class OrderStatusChangedConsumer(IOrderNotifier notifier) : IConsumer<OrderStatusChanged>
{
    public Task Consume(ConsumeContext<OrderStatusChanged> context)
    {
        var m = context.Message;
        return notifier.OrderStatusChangedAsync(
            new OrderStatusNotification(m.OrderId, m.OrderNumber, m.CustomerId, m.PharmacyId, m.NewStatus, m.OccurredAt),
            context.CancellationToken);
    }
}
