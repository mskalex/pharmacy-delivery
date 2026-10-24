using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PharmacyDelivery.Services.Diagnostics;

/// <summary>
/// Custom OpenTelemetry sources. Tags never contain personal data (no addresses, emails or phone numbers).
/// The names are registered in the composition root (AddSource/AddMeter).
/// </summary>
public static class PharmacyDeliveryTelemetry
{
    public const string SourceName = "PharmacyDelivery";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    public static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("pharmacy_delivery.orders.created", description: "Orders created at checkout.");

    public static readonly Counter<long> OrdersDelivered =
        Meter.CreateCounter<long>("pharmacy_delivery.orders.delivered", description: "Orders delivered to customers.");

    public static readonly Histogram<double> CheckoutDuration =
        Meter.CreateHistogram<double>("pharmacy_delivery.checkout.duration", unit: "ms", description: "Checkout processing time.");

    public static readonly Histogram<double> DeliveryDuration =
        Meter.CreateHistogram<double>("pharmacy_delivery.delivery.duration", unit: "min", description: "Time from payment to delivery.");

    public static readonly Counter<long> ReservationConflicts =
        Meter.CreateCounter<long>("pharmacy_delivery.stock.reservation_conflicts",
            description: "Failed reservation attempts (tag: reason).");

    public static readonly Counter<long> ReservationsExpired =
        Meter.CreateCounter<long>("pharmacy_delivery.reservations.expired", description: "Unpaid reservations released by the expiry job.");

    public static readonly Counter<long> PharmacyAcceptanceTimeouts =
        Meter.CreateCounter<long>("pharmacy_delivery.pharmacy.acceptance_timeouts",
            description: "Orders reassigned because a pharmacy did not accept in time.");

    public static readonly Counter<long> DispatchNoCourier =
        Meter.CreateCounter<long>("pharmacy_delivery.dispatch.no_courier", description: "Dispatch attempts without an available courier.");

    public static readonly Counter<long> TrackingPointsReceived =
        Meter.CreateCounter<long>("pharmacy_delivery.tracking.points_received", description: "GPS points accepted.");

    public static readonly Counter<long> TrackingPointsDropped =
        Meter.CreateCounter<long>("pharmacy_delivery.tracking.points_dropped", description: "GPS points dropped (tag: reason).");

    public static readonly UpDownCounter<long> TrackingConnections =
        Meter.CreateUpDownCounter<long>("pharmacy_delivery.tracking.connections", description: "Active SignalR tracking subscriptions.");

    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) =>
        ActivitySource.StartActivity(name, kind);
}
