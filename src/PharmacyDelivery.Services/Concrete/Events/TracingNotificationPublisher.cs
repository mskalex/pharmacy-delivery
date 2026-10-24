using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>
/// Notification pipeline (MediatR behaviors apply to requests only): every handler runs sequentially inside a
/// tracing span, and failures are logged with the event and handler names before they abort the transaction.
/// </summary>
public sealed class TracingNotificationPublisher(ILogger<TracingNotificationPublisher> logger) : INotificationPublisher
{
    public async Task Publish(IEnumerable<NotificationHandlerExecutor> handlerExecutors, INotification notification,
        CancellationToken cancellationToken)
    {
        var eventName = notification is IDomainEventNotification wrapped ? wrapped.EventName : notification.GetType().Name;
        foreach (var executor in handlerExecutors)
        {
            var handlerName = executor.HandlerInstance.GetType().Name;
            using var activity = PharmacyDeliveryTelemetry.StartActivity("domain-event " + eventName);
            activity?.SetTag("handler", handlerName);
            try
            {
                await executor.HandlerCallback(notification, cancellationToken);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Domain event handler {Handler} failed for {Event}", handlerName, eventName);
                throw;
            }
        }
    }
}
