using MediatR;
using Microsoft.Extensions.Logging;
using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>Business log of order lifecycle (one line per transition, no personal data).</summary>
public sealed class LogOrderStatusChangeHandler(ILogger<LogOrderStatusChangeHandler> logger)
    : INotificationHandler<DomainEventNotification<OrderStatusChangedDomainEvent>>
{
    public Task Handle(DomainEventNotification<OrderStatusChangedDomainEvent> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        logger.LogInformation(
            "Order {OrderNumber} status {PreviousStatus} -> {NewStatus} (pharmacy {PharmacyId})",
            e.OrderNumber,
            e.PreviousStatus?.ToString() ?? "(new)",
            e.NewStatus,
            e.PharmacyId);
        return Task.CompletedTask;
    }
}
