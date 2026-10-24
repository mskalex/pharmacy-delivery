using MassTransit;
using PharmacyDelivery.Messaging.Contracts.V1;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>Order packed → find the nearest available courier.</summary>
public sealed class OrderReadyForPickupConsumer(IDispatchService dispatch) : IConsumer<OrderReadyForPickup>
{
    public Task Consume(ConsumeContext<OrderReadyForPickup> context) =>
        dispatch.DispatchAsync(context.Message.OrderId, context.CancellationToken);
}
