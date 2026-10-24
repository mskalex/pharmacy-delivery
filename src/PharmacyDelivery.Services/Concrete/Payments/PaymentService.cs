using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Payments;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Payment flow. External PSP calls are never made inside a database transaction: the refund is requested first
/// and recorded in a second, short transaction.
/// </summary>
public sealed class PaymentService(
    IOrderRepository orders,
    IPaymentRepository payments,
    IStockReservationService reservations,
    IPaymentGateway gateway,
    IUnitOfWork unitOfWork,
    IClock clock,
    IOptions<PaymentsOptions> options,
    ILogger<PaymentService> logger) : IPaymentService
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    private readonly PaymentsOptions _options = options.Value;

    public async Task<PaymentStartResult> StartAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(new OrderId(orderId), cancellationToken);
        if (order is null || order.CustomerId.Value != customerId)
            throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {orderId} was not found.");

        if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.PaymentFailed))
        {
            throw new BusinessRuleException(ErrorCodes.OrderInvalidTransition,
                $"Order {order.Number} cannot be paid in status {order.Status}.");
        }

        var initiation = await gateway.CreatePaymentAsync(orderId, order.Number, order.Total, cancellationToken);
        var payment = Payment.Start(order.Id, gateway.Provider, initiation.ProviderPaymentId, order.Total, clock.UtcNow);
        payments.Add(payment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PaymentStartResult(payment.Id, payment.ProviderPaymentId, initiation.RedirectUrl, payment.Amount.Amount,
            payment.Amount.Currency);
    }

    public async Task<string> HandleWebhookAsync(string rawBody, long timestamp, string? signature, CancellationToken cancellationToken)
    {
        if (!HmacSignature.Verify(
                _options.WebhookSecret,
                timestamp,
                rawBody,
                signature,
                clock.UtcNow,
                TimeSpan.FromSeconds(_options.SignatureToleranceSeconds)))
        {
            logger.LogWarning("Rejected PSP webhook with an invalid signature or timestamp");
            throw new UnauthorizedException(ErrorCodes.PaymentInvalidSignature, "Invalid webhook signature.");
        }

        PaymentNotification? notification;
        try
        {
            notification = JsonSerializer.Deserialize<PaymentNotification>(rawBody, PharmacyDeliveryJsonOptions.Tolerant);
        }
        catch (JsonException)
        {
            notification = null;
        }

        if (notification is null || string.IsNullOrWhiteSpace(notification.ProviderPaymentId))
            throw ValidationFailedException.ForField("body", ErrorCodes.ValidationFailed, "Malformed payment notification.");

        return await ProcessAsync(notification, cancellationToken);
    }

    public async Task RefundCancelledOrderAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        var payment = await payments.FindSucceededForOrderAsync(orderId, cancellationToken);
        if (payment is null)
            return;

        await gateway.RefundAsync(payment.ProviderPaymentId, payment.Amount, cancellationToken);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await orders.GetForUpdateAsync(orderId, ct);
                var current = await payments.FindByProviderIdAsync(payment.Provider, payment.ProviderPaymentId, ct);
                if (order is null || current is null || current.Status != PaymentStatus.Succeeded)
                    return false;

                var now = clock.UtcNow;
                current.MarkRefunded(now);
                if (order.Status == OrderStatus.Cancelled)
                    order.MarkRefunded(now);

                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);

        logger.LogInformation("Payment of order {OrderId} refunded", orderId);
    }

    private async Task<string> ProcessAsync(PaymentNotification notification, CancellationToken cancellationToken)
    {
        var probe = await payments.FindByProviderIdAsync(gateway.Provider, notification.ProviderPaymentId, cancellationToken)
                    ?? throw new NotFoundException(ErrorCodes.PaymentNotFound, "Unknown payment.");

        if (notification.Amount != probe.Amount.Amount || notification.Currency != probe.Amount.Currency)
            throw new BusinessRuleException(ErrorCodes.PaymentAmountMismatch, "Notified amount differs from the payment amount.");

        var orderId = probe.OrderId;
        var outcome = await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // Order row first: serializes this webhook with duplicates and with the reservation expiry job.
                var order = await orders.GetForUpdateAsync(orderId, ct)
                            ?? throw new NotFoundException(ErrorCodes.OrderNotFound, $"Order {orderId} was not found.");
                var payment = await payments.FindByProviderIdAsync(gateway.Provider, notification.ProviderPaymentId, ct)
                              ?? throw new NotFoundException(ErrorCodes.PaymentNotFound, "Unknown payment.");
                var now = clock.UtcNow;

                if (notification.Status == Succeeded)
                {
                    if (!payment.MarkSucceeded(now))
                        return "duplicate";

                    if (order.Status is OrderStatus.AwaitingPayment or OrderStatus.PaymentFailed)
                    {
                        order.MarkPaid(now);
                        await reservations.ConfirmPaidAsync(order.Id, ct);
                        await unitOfWork.SaveChangesAsync(ct);
                        return "paid";
                    }

                    // Reservation expired (order cancelled) or the order was paid by another payment: refund.
                    await unitOfWork.SaveChangesAsync(ct);
                    return "refund";
                }

                if (!payment.MarkFailed(now))
                    return "duplicate";

                if (order.Status == OrderStatus.AwaitingPayment)
                    order.MarkPaymentFailed(now);

                await unitOfWork.SaveChangesAsync(ct);
                return "failed";
            },
            cancellationToken);

        if (outcome == "refund")
        {
            logger.LogWarning("Payment for order {OrderId} arrived after cancellation; refunding", orderId);
            await RefundLatePaymentAsync(orderId, notification.ProviderPaymentId, cancellationToken);
        }

        logger.LogInformation("PSP notification for order {OrderId} processed: {Outcome}", orderId, outcome);
        return outcome;
    }

    private async Task RefundLatePaymentAsync(OrderId orderId, string providerPaymentId, CancellationToken cancellationToken)
    {
        var payment = await payments.FindByProviderIdAsync(gateway.Provider, providerPaymentId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Succeeded)
            return;

        await gateway.RefundAsync(providerPaymentId, payment.Amount, cancellationToken);
        await unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await orders.GetForUpdateAsync(orderId, ct);
                var current = await payments.FindByProviderIdAsync(gateway.Provider, providerPaymentId, ct);
                if (current is not { Status: PaymentStatus.Succeeded })
                    return false;

                var now = clock.UtcNow;
                current.MarkRefunded(now);
                if (order is { Status: OrderStatus.Cancelled })
                    order.MarkRefunded(now);

                await unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            cancellationToken);
    }
}
