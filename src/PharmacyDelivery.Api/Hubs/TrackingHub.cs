using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Api.Hubs;

/// <summary>Messages pushed to clients (method names are part of the v1 contract).</summary>
public static class TrackingHubMethods
{
    /// <summary>Order status changed; payload <see cref="OrderStatusMessage"/>. Sent to the order and pharmacy groups.</summary>
    public const string OrderStatusChanged = "orderStatusChanged";

    /// <summary>New courier position with a fresh ETA; payload <see cref="CourierPositionMessage"/>. Sent only while InDelivery.</summary>
    public const string CourierPosition = "courierPosition";
}

/// <summary>Payload of <c>orderStatusChanged</c>: the new status (camelCase) and when it changed.</summary>
public sealed record OrderStatusMessage(Guid OrderId, string OrderNumber, string Status, DateTimeOffset OccurredAt);

/// <summary>Payload of <c>courierPosition</c>: the latest accepted GPS fix and the recalculated ETA.</summary>
public sealed record CourierPositionMessage(
    Guid OrderId,
    double Latitude,
    double Longitude,
    DateTimeOffset RecordedAt,
    DateTimeOffset EstimatedDeliveryAt);

/// <summary>
/// Real-time tracking hub (<c>/hubs/v1/tracking</c>). A client subscribes to an order it may see (resource-based
/// check, same rule as GET /orders/{id}); pharmacists automatically receive status changes of their pharmacy.
/// The browser passes the JWT in the <c>access_token</c> query parameter (WebSockets cannot send headers).
/// </summary>
[Authorize]
public sealed class TrackingHub(ITrackingService tracking) : Hub
{
    private PrincipalCurrentUser CurrentUser => new(Context.User);

    /// <summary>SignalR group of everyone subscribed to an order (its customer and anyone else allowed to see it).</summary>
    public static string OrderGroup(Guid orderId) => "order:" + orderId.ToString("N");

    /// <summary>SignalR group of a pharmacy's pharmacists (status changes of the pharmacy's orders).</summary>
    public static string PharmacyGroup(Guid pharmacyId) => "pharmacy:" + pharmacyId.ToString("N");

    /// <summary>Counts the connection and puts a pharmacist into the group of their pharmacy.</summary>
    public override async Task OnConnectedAsync()
    {
        PharmacyDeliveryTelemetry.TrackingConnections.Add(1);
        if (CurrentUser.Role == Roles.Pharmacist && CurrentUser.PharmacyId is { } pharmacyId)
            await Groups.AddToGroupAsync(Context.ConnectionId, PharmacyGroup(pharmacyId));

        await base.OnConnectedAsync();
    }

    /// <summary>Decrements the connection metric; SignalR removes the connection from its groups.</summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        PharmacyDeliveryTelemetry.TrackingConnections.Add(-1);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribes the connection to status and courier position updates of an order and returns the current snapshot.
    /// An order the caller may not see fails with "Order not found." (no hint that it exists).
    /// </summary>
    public async Task<TrackingResponse> SubscribeToOrder(Guid orderId)
    {
        if (!await tracking.CanSubscribeAsync(orderId, CurrentUser, Context.ConnectionAborted))
            throw new HubException("Order not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, OrderGroup(orderId), Context.ConnectionAborted);
        var snapshot = await tracking.GetSnapshotAsync(orderId, CurrentUser, Context.ConnectionAborted);
        return snapshot.ToResponse();
    }

    /// <summary>Stops updates of an order for this connection (e.g. when the client switches to another order).</summary>
    public Task UnsubscribeFromOrder(Guid orderId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, OrderGroup(orderId), Context.ConnectionAborted);
}

/// <summary>Pushes notifications through SignalR (Redis backplane delivers them to clients on any instance).</summary>
public sealed class SignalROrderNotifier(IHubContext<TrackingHub> hub) : IOrderNotifier
{
    /// <summary>Sends <c>orderStatusChanged</c> to the order's subscribers and to the pharmacists of its pharmacy.</summary>
    public Task OrderStatusChangedAsync(OrderStatusNotification notification, CancellationToken cancellationToken)
    {
        var message = new OrderStatusMessage(notification.OrderId, notification.OrderNumber, notification.Status, notification.OccurredAt);
        return Task.WhenAll(
            hub.Clients.Group(TrackingHub.OrderGroup(notification.OrderId))
                .SendAsync(TrackingHubMethods.OrderStatusChanged, message, cancellationToken),
            hub.Clients.Group(TrackingHub.PharmacyGroup(notification.PharmacyId))
                .SendAsync(TrackingHubMethods.OrderStatusChanged, message, cancellationToken));
    }

    /// <summary>Sends <c>courierPosition</c> to the order's subscribers only (never to the pharmacy group).</summary>
    public Task CourierPositionChangedAsync(CourierPositionNotification notification, CancellationToken cancellationToken) =>
        hub.Clients.Group(TrackingHub.OrderGroup(notification.OrderId)).SendAsync(
            TrackingHubMethods.CourierPosition,
            new CourierPositionMessage(notification.OrderId, notification.Latitude, notification.Longitude, notification.RecordedAt,
                notification.EstimatedDeliveryAt),
            cancellationToken);
}
