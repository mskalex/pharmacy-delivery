using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

public sealed class PharmacyOrderService(
    IOrderQueries queries,
    IOrderRepository orders,
    IPharmacySelector pharmacySelector,
    IStockReservationService reservations,
    IPaymentService payments,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<PharmacyOptions> pharmacyOptions,
    IOptions<CheckoutOptions> checkoutOptions,
    ILogger<PharmacyOrderService> logger) : IPharmacyOrderService
{
    private readonly PharmacyOptions _pharmacy = pharmacyOptions.Value;
    private readonly CheckoutOptions _checkout = checkoutOptions.Value;

    private enum ReassignOutcome
    {
        Reassigned,
        Cancelled,
    }

    public Task<IReadOnlyList<PharmacyOrderSummary>> GetQueueAsync(Guid pharmacyId, CancellationToken cancellationToken) =>
        queries.GetPharmacyQueueAsync(pharmacyId, TimeSpan.FromMinutes(_pharmacy.AcceptanceTimeoutMinutes), cancellationToken);

    public Task AssignAfterPaymentAsync(Guid orderId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await orders.GetForUpdateAsync(new OrderId(orderId), ct);
                if (order is not { Status: OrderStatus.Paid })
                {
                    return false; // already assigned or cancelled: duplicate delivery of the event
                }

                order.AssignToPharmacy(clock.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);

    public Task AcceptAsync(Guid pharmacyId, Guid orderId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await LoadOwnedAsync(pharmacyId, orderId, ct);
                order.Accept(clock.UtcNow);
                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);

    public async Task RejectAsync(Guid pharmacyId, Guid orderId, string reason, CancellationToken cancellationToken)
    {
        var outcome = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await LoadOwnedAsync(pharmacyId, orderId, ct);
                if (order.Status != OrderStatus.AssignedToPharmacy)
                {
                    throw new BusinessRuleException(ErrorCodes.OrderInvalidTransition,
                        $"Order {order.Number} cannot be rejected in status {order.Status}.");
                }

                var result = await ReassignAsync(order, $"Rejected by pharmacy: {reason}", ct);
                await unitOfWork.SaveChangesAsync(ct);
                return result;
            },
            cancellationToken);

        if (outcome == ReassignOutcome.Cancelled)
            await payments.RefundCancelledOrderAsync(new OrderId(orderId), cancellationToken);
    }

    public Task MarkReadyAsync(Guid pharmacyId, Guid orderId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await LoadOwnedAsync(pharmacyId, orderId, ct);
                var now = clock.UtcNow;
                order.MarkReadyForPickup(now);
                await reservations.CommitForOrderAsync(order.Id, now, ct);
                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);

    public async Task<int> ProcessAcceptanceTimeoutsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var threshold = clock.UtcNow.AddMinutes(-_pharmacy.AcceptanceTimeoutMinutes);
        var candidates = await orders.FindAcceptanceTimeoutsAsync(threshold, batchSize, cancellationToken);
        var processed = 0;

        foreach (var orderId in candidates)
        {
            var outcome = await unitOfWork.ExecuteInTransactionAsync<ReassignOutcome?>(
                async ct =>
                {
                    var order = await orders.TryGetForUpdateSkipLockedAsync(orderId, ct);
                    if (order is null || order.Status != OrderStatus.AssignedToPharmacy || order.AssignedAt > threshold)
                    {
                        return null; // locked by someone else or accepted in the meantime
                    }

                    PharmacyDeliveryTelemetry.PharmacyAcceptanceTimeouts.Add(1);
                    var result = await ReassignAsync(order, "Pharmacy did not accept the order in time.", ct);
                    await unitOfWork.SaveChangesAsync(ct);
                    return result;
                },
                cancellationToken);

            if (outcome is null)
                continue;

            processed++;
            if (outcome == ReassignOutcome.Cancelled)
                await payments.RefundCancelledOrderAsync(orderId, cancellationToken);
        }

        return processed;
    }

    /// <summary>Moves a locked order to the next pharmacy able to fulfil it, or cancels it. Runs inside the caller's transaction.</summary>
    private async Task<ReassignOutcome> ReassignAsync(Order order, string reason, CancellationToken cancellationToken)
    {
        using var activity = PharmacyDeliveryTelemetry.StartActivity("pharmacy.reassign");
        activity?.SetTag("order.id", order.Id.Value);

        var now = clock.UtcNow;
        var previous = order.PharmacyId;
        order.MarkRejectedBy(previous);
        await reservations.ReleaseForOrderAsync(order.Id, now, cancellationToken);

        var lines = order.Items.Select(i => (i.ProductId, i.Quantity)).ToList();
        var selection = await pharmacySelector.SelectAsync(
            order.DeliveryAddress.Location,
            lines,
            order.RejectedPharmacyIds,
            now,
            cancellationToken);

        foreach (var ranked in selection.Ranked.Take(_checkout.MaxPharmacyAttempts))
        {
            try
            {
                // The order is paid, so the new reservation does not expire.
                await reservations.ReserveAsync(order.Id, ranked.Pharmacy.Id, lines, now, expiresAt: null, cancellationToken);
                order.Reassign(ranked.Pharmacy.Id, now, now.Add(ranked.EstimatedDelivery), reason);
                logger.LogInformation(
                    "Order {OrderNumber} reassigned from pharmacy {From} to {To}: {Reason}",
                    order.Number,
                    previous,
                    ranked.Pharmacy.Id,
                    reason);
                activity?.SetTag("result", "reassigned");
                return ReassignOutcome.Reassigned;
            }
            catch (DomainException ex) when (ex.Code == ErrorCodes.StockInsufficient)
            {
                // StockAllocator checks before mutating, so trying the next pharmacy in the same transaction is safe.
                order.MarkRejectedBy(ranked.Pharmacy.Id);
            }
        }

        order.Cancel("No pharmacy could fulfil the order.", now);
        logger.LogWarning("Order {OrderNumber} cancelled: no pharmacy could fulfil it", order.Number);
        activity?.SetTag("result", "cancelled");
        return ReassignOutcome.Cancelled;
    }

    private async Task<Order> LoadOwnedAsync(Guid pharmacyId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(new OrderId(orderId), cancellationToken);
        if (order is null || order.PharmacyId.Value != pharmacyId)
            throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {orderId} was not found.");

        return order;
    }
}
