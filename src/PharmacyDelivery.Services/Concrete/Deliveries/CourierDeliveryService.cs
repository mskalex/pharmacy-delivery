using Microsoft.Extensions.Logging;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;

namespace PharmacyDelivery.Services.Concrete;

public sealed class CourierDeliveryService(
    IDeliveryQueries queries,
    IDeliveryRepository deliveries,
    IOrderRepository orders,
    ICourierRepository couriers,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<CourierDeliveryService> logger) : ICourierDeliveryService
{
    public Task<CurrentDeliveryInfo?> GetCurrentAsync(Guid courierId, CancellationToken cancellationToken) =>
        queries.GetCurrentForCourierAsync(courierId, cancellationToken);

    public Task PickUpAsync(Guid courierId, Guid deliveryId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var (delivery, order) = await LoadAsync(courierId, deliveryId, ct);
                var now = clock.UtcNow;
                delivery.PickUp(now);
                order.MarkInDelivery(now);
                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);

    public async Task DeliverAsync(Guid courierId, Guid deliveryId, CancellationToken cancellationToken)
    {
        var paidAt = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var (delivery, order) = await LoadAsync(courierId, deliveryId, ct);
                var now = clock.UtcNow;
                delivery.Complete(now);
                order.MarkDelivered(now);

                var courier = await couriers.GetAsync(delivery.CourierId, ct);
                courier?.MarkAvailable();

                await unitOfWork.SaveChangesAsync(ct);
                return order.PaidAt;
            },
            cancellationToken);

        PharmacyDeliveryTelemetry.OrdersDelivered.Add(1);
        if (paidAt is { } paid)
            PharmacyDeliveryTelemetry.DeliveryDuration.Record((clock.UtcNow - paid).TotalMinutes);

        logger.LogInformation("Delivery {DeliveryId} completed by courier {CourierId}", deliveryId, courierId);
    }

    private async Task<(Delivery Delivery, Order Order)> LoadAsync(Guid courierId, Guid deliveryId, CancellationToken cancellationToken)
    {
        var delivery = await deliveries.GetAsync(new DeliveryId(deliveryId), cancellationToken);
        if (delivery is null || delivery.CourierId.Value != courierId)
            throw new NotFoundException(ErrorCodes.DeliveryNotFound, $"Delivery {deliveryId} was not found.");

        // Lock order row (lock ordering: order first).
        var order = await orders.GetForUpdateAsync(delivery.OrderId, cancellationToken)
                    ?? throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {delivery.OrderId} was not found.");
        return (delivery, order);
    }
}
