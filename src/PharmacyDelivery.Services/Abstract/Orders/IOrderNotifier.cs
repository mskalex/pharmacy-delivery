using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Real-time push to clients (SignalR).</summary>
public interface IOrderNotifier
{
    /// <summary>Pushes a status change to the customer and to the pharmacy panel.</summary>
    Task OrderStatusChangedAsync(OrderStatusNotification notification, CancellationToken cancellationToken);

    /// <summary>Pushes the courier position and ETA to the customer tracking the order.</summary>
    Task CourierPositionChangedAsync(CourierPositionNotification notification, CancellationToken cancellationToken);
}
