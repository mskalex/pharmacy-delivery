using MassTransit;
using PharmacyDelivery.Messaging.Contracts.V1;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Infrastructure.Concrete;

// Consumers are thin: they call a service. Each runs inside the EF Core outbox/inbox transaction, so a message
// redelivered by RabbitMQ is detected by the inbox and processed once (DESIGN.md §11).

/// <summary>Payment captured → hand the order to its pharmacy.</summary>
public sealed class OrderPaidConsumer(IPharmacyOrderService pharmacyOrders) : IConsumer<OrderPaid>
{
    public Task Consume(ConsumeContext<OrderPaid> context) =>
        pharmacyOrders.AssignAfterPaymentAsync(context.Message.OrderId, context.CancellationToken);
}
