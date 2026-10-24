using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Concrete;
using PharmacyDelivery.Services.Models;
using Moq;

namespace PharmacyDelivery.Tests.Services;

[TestFixture]
public sealed class TrackingServiceTests : TestBase
{
    private readonly Guid _courier = Guid.NewGuid();
    private TrackingService _service = null!;

    private Mock<ICourierLocationStore> Locations => MockOf<ICourierLocationStore>();

    private Mock<IDeliveryQueries> Deliveries => MockOf<IDeliveryQueries>();

    private Mock<IOrderNotifier> Notifier => MockOf<IOrderNotifier>();

    private Mock<ILocationHistoryQueue> History => MockOf<ILocationHistoryQueue>();

    [SetUp]
    public void SetUp()
    {
        _service = Service<TrackingService>();
        History.Setup(h => h.TryEnqueue(It.IsAny<LocationHistoryPoint>())).Returns(true);
        MockOf<IEtaService>()
            .Setup(e => e.EstimateTravelTimeAsync(It.IsAny<GeoPoint>(), It.IsAny<GeoPoint>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TimeSpan.FromMinutes(7));
    }

    private static LocationPointInput Point(double lat, double lng, int secondsAgo) => new(lat, lng, Now.AddSeconds(-secondsAgo), 20);

    [Test]
    [Property("TestCase", "TC-7.8.1")]
    public async Task Ingest_DropsFutureStaleOutOfOrderAndOutlierPoints()
    {
        Locations.Setup(l => l.GetAsync(_courier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CourierPosition(_courier, 40.7500, -73.9900, Now.AddSeconds(-60)));

        var result = await _service.IngestAsync(_courier,
        [
            Point(40.7501, -73.9899, 30), // ok
            Point(40.7502, -73.9898, -120), // 2 minutes in the future
            Point(40.7502, -73.9898, 3600), // stale (older than 10 min)
            Point(40.7501, -73.9899, 90), // older than the last stored point
            Point(40.8100, -73.8300, 20), // ~15 km in 10 s: GPS outlier
            Point(40.7503, -73.9897, 10), // ok
        ], Ct);

        Assert.That((result.Accepted, result.Dropped), Is.EqualTo((2, 4)));
        Locations.Verify(l => l.UpdateAsync(It.Is<CourierPosition>(p => p.Latitude == 40.7503), It.IsAny<CancellationToken>()), Times.Once);
        History.Verify(h => h.TryEnqueue(It.IsAny<LocationHistoryPoint>()), Times.Exactly(2));
    }

    [Test]
    [Property("TestCase", "TC-7.8.2")]
    public async Task Ingest_PushesPositionOnlyWhileOrderIsInDelivery()
    {
        var orderId = Guid.NewGuid();
        Deliveries.SetupSequence(d => d.GetActiveRefForCourierAsync(_courier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveDeliveryRef(Guid.NewGuid(), orderId, Guid.NewGuid(), DeliveryStatus.Assigned, 40.75, -73.98))
            .ReturnsAsync(new ActiveDeliveryRef(Guid.NewGuid(), orderId, Guid.NewGuid(), DeliveryStatus.InDelivery, 40.75, -73.98));

        await _service.IngestAsync(_courier, [Point(40.7501, -73.9899, 5)], Ct);
        Notifier.Verify(n => n.CourierPositionChangedAsync(It.IsAny<CourierPositionNotification>(), It.IsAny<CancellationToken>()),
            Times.Never);

        await _service.IngestAsync(_courier, [Point(40.7502, -73.9898, 3)], Ct);
        Notifier.Verify(n => n.CourierPositionChangedAsync(
            It.Is<CourierPositionNotification>(p => p.OrderId == orderId && p.EstimatedDeliveryAt == Now.AddMinutes(7)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Property("TestCase", "TC-7.8.3")]
    public void Ingest_RejectsTooLargeBatch()
    {
        var points = Enumerable.Range(0, 51).Select(i => Point(40.75, -73.99, 60 - i)).ToList();

        Assert.ThrowsAsync<ValidationFailedException>(() => _service.IngestAsync(_courier, points, Ct));
    }

    [Test]
    [Property("TestCase", "TC-7.8.4")]
    public void PushThrottle_LimitsPushesPerOrder_AndRecalculatesEtaPeriodically()
    {
        var throttle = new TrackingPushThrottle();
        var order = Guid.NewGuid();
        var t = Now;
        var interval = TimeSpan.FromSeconds(1);
        var eta = TimeSpan.FromSeconds(30);

        var first = throttle.Evaluate(order, t, interval, eta);
        throttle.StoreEta(order, t, t.AddMinutes(9));
        var tooSoon = throttle.Evaluate(order, t.AddMilliseconds(300), interval, eta);
        var later = throttle.Evaluate(order, t.AddSeconds(2), interval, eta);
        var etaDue = throttle.Evaluate(order, t.AddSeconds(31), interval, eta);

        Assert.Multiple(() =>
        {
            Assert.That((first.Push, first.RecalculateEta), Is.EqualTo((true, true)));
            Assert.That(tooSoon.Push, Is.False);
            Assert.That((later.Push, later.RecalculateEta, later.CachedEta), Is.EqualTo((true, false, (DateTimeOffset?)t.AddMinutes(9))));
            Assert.That(etaDue.RecalculateEta, Is.True);
        });
    }
}
