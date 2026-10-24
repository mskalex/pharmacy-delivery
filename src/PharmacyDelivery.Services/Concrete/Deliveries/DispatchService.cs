using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Courier assignment. Candidates come from the Redis GEO index (nearest first). Each candidate is protected by a
/// short Redis lock so two instances never offer the same courier at once; the database unique partial index
/// (one active delivery per courier) is the final guarantee.
/// </summary>
public sealed class DispatchService(
    IOrderQueries orderQueries,
    IPharmacyQueries pharmacyQueries,
    IDeliveryQueries deliveryQueries,
    IOrderRepository orders,
    ICourierRepository couriers,
    IDeliveryRepository deliveries,
    ICourierLocationStore locations,
    IDistributedLock distributedLock,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<DispatchOptions> options,
    ILogger<DispatchService> logger) : IDispatchService
{
    private readonly DispatchOptions _options = options.Value;

    public async Task<DispatchOutcome> DispatchAsync(Guid orderId, CancellationToken cancellationToken)
    {
        using var activity = PharmacyDeliveryTelemetry.StartActivity("dispatch.assign");
        activity?.SetTag("order.id", orderId);

        var access = await orderQueries.GetAccessInfoAsync(orderId, cancellationToken);
        if (access is not { Status: OrderStatus.ReadyForPickup })
        {
            activity?.SetTag("result", "not_ready");
            return DispatchOutcome.NotReady;
        }

        var pharmacy = await pharmacyQueries.GetInfoAsync(access.PharmacyId, cancellationToken)
                       ?? throw new NotFoundException(ErrorCodes.PharmacyNotFound, $"Pharmacy {access.PharmacyId} was not found.");

        var now = clock.UtcNow;
        var nearby = (await locations.FindNearestAsync(
                new GeoPoint(pharmacy.Latitude, pharmacy.Longitude),
                _options.SearchRadiusKm,
                _options.MaxCandidates,
                cancellationToken))
            .Where(c => now - c.RecordedAt <= TimeSpan.FromSeconds(_options.MaxPositionAgeSeconds))
            .ToList();

        var available = (await deliveryQueries.GetAvailableCourierIdsAsync(nearby.Select(c => c.CourierId).ToList(), cancellationToken))
            .ToHashSet();

        foreach (var candidate in nearby.Where(c => available.Contains(c.CourierId)))
        {
            await using var handle = await distributedLock.TryAcquireAsync(
                $"courier:{candidate.CourierId}",
                TimeSpan.FromSeconds(_options.CourierLockSeconds),
                cancellationToken);
            if (handle is null)
            {
                continue; // being assigned by another instance right now
            }

            try
            {
                var assigned = await TryAssignAsync(new OrderId(orderId), new CourierId(candidate.CourierId), cancellationToken);
                if (assigned is null)
                {
                    activity?.SetTag("result", "not_ready");
                    return DispatchOutcome.NotReady;
                }

                if (assigned.Value)
                {
                    activity?.SetTag("result", "assigned");
                    activity?.SetTag("courier.id", candidate.CourierId);
                    logger.LogInformation("Courier {CourierId} assigned to order {OrderId}", candidate.CourierId, orderId);
                    return DispatchOutcome.Assigned;
                }
            }
            catch (ConflictException ex) when (ex.Code is ErrorCodes.CourierBusy or ErrorCodes.DeliveryDuplicate
                                                   or ErrorCodes.ConcurrencyConflict)
            {
                logger.LogInformation("Courier {CourierId} could not be assigned: {Code}", candidate.CourierId, ex.Code);
            }
        }

        PharmacyDeliveryTelemetry.DispatchNoCourier.Add(1);
        activity?.SetTag("result", "no_courier");
        logger.LogWarning("No available courier for order {OrderId} (candidates nearby: {Count})", orderId, nearby.Count);
        return DispatchOutcome.NoCourier;
    }

    public async Task<int> RetryPendingAsync(int batchSize, CancellationToken cancellationToken)
    {
        var pending = await orders.FindAwaitingDispatchAsync(clock.UtcNow.AddSeconds(-10), batchSize, cancellationToken);
        var assigned = 0;
        foreach (var orderId in pending)
        {
            if (await DispatchAsync(orderId.Value, cancellationToken) == DispatchOutcome.Assigned)
                assigned++;
        }

        return assigned;
    }

    /// <returns>true — assigned; false — courier not available; null — order no longer waits for a courier.</returns>
    private Task<bool?> TryAssignAsync(OrderId orderId, CourierId courierId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync<bool?>(
            async ct =>
            {
                var order = await orders.GetForUpdateAsync(orderId, ct);
                if (order is not { Status: OrderStatus.ReadyForPickup })
                    return null;

                var courier = await couriers.GetAsync(courierId, ct);
                if (courier is not { Status: CourierStatus.Available })
                    return false;

                var now = clock.UtcNow;
                courier.MarkBusy();
                deliveries.Add(Delivery.Assign(DeliveryId.New(), order.Id, courier.Id, order.PharmacyId, now));
                order.MarkCourierAssigned(now);
                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);
}
