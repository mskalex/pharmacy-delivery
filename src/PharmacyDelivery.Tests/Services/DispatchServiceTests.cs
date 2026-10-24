using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Concrete;
using PharmacyDelivery.Services.Models;
using Moq;

namespace PharmacyDelivery.Tests.Services;

[TestFixture]
public sealed class DispatchServiceTests : TestBase
{
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _pharmacyId = Guid.NewGuid();
    private DispatchService _service = null!;

    private Mock<ICourierLocationStore> Locations => MockOf<ICourierLocationStore>();

    private Mock<IDeliveryQueries> DeliveryQueries => MockOf<IDeliveryQueries>();

    private Mock<IDistributedLock> Lock => MockOf<IDistributedLock>();

    [SetUp]
    public void SetUp()
    {
        _service = Service<DispatchService>();
        MockOf<IOrderQueries>().Setup(q => q.GetAccessInfoAsync(_orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderAccessInfo(_orderId, Guid.NewGuid(), _pharmacyId, OrderStatus.ReadyForPickup, null));
        MockOf<IPharmacyQueries>().Setup(p => p.GetInfoAsync(_pharmacyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PharmacyInfo(_pharmacyId, "T", "T", "s", "c", "10001", 40.75, -73.99));
    }

    [Test]
    [Property("TestCase", "TC-7.7.2")]
    public async Task StalePositionsAndLockedCouriers_AreSkipped_ResultIsNoCourier()
    {
        var stale = Guid.NewGuid();
        var locked = Guid.NewGuid();
        Locations.Setup(l => l.FindNearestAsync(It.IsAny<GeoPoint>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NearbyCourier(stale, 0.3, Now.AddMinutes(-10)), new NearbyCourier(locked, 0.5, Now.AddSeconds(-5))]);
        DeliveryQueries.Setup(d => d.GetAvailableCourierIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.ToList());
        Lock.Setup(l => l.TryAcquireAsync($"courier:{locked}", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        var outcome = await _service.DispatchAsync(_orderId, Ct);

        Assert.That(outcome, Is.EqualTo(DispatchOutcome.NoCourier));
        DeliveryQueries.Verify(d =>
            d.GetAvailableCourierIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => !ids.Contains(stale)), It.IsAny<CancellationToken>()));
    }
}
