using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Services;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Concrete;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Tests.Domain;
using Moq;

namespace PharmacyDelivery.Tests.Services;

[TestFixture]
public sealed class CheckoutServiceTests : TestBase
{
    private static readonly AddressInput Address = new("11 E 36th St", "New York", "10016", 40.7493, -73.9822);
    private readonly Guid _customer = Guid.NewGuid();

    private readonly ProductDetails _product = new(Guid.NewGuid(), "Acetaminophen", "0030000000001", ProductCategory.PainRelief, "", 6.49m,
        "USD", true);

    private CheckoutService _service = null!;

    private Mock<ICartStore> Cart => MockOf<ICartStore>();

    private Mock<IPharmacySelector> Selector => MockOf<IPharmacySelector>();

    private Mock<IStockReservationService> Reservations => MockOf<IStockReservationService>();

    private Mock<IOrderRepository> Orders => MockOf<IOrderRepository>();

    [SetUp]
    public void SetUp()
    {
        _service = Service<CheckoutService>();
        Cart.Setup(c => c.GetAsync(_customer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Cart { Items = [new CartItem { ProductId = _product.Id, Quantity = 2 }] });
        MockOf<ICatalogQueries>().Setup(c => c.GetManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([_product]);
    }

    private static RankedPharmacy Ranked(string name)
    {
        var pharmacy = Pharmacy.Create(PharmacyId.New(), name, name, "1 Test Street", "New York", "10001", new GeoPoint(40.75, -73.99),
            OpeningHours.NonStop());
        return new RankedPharmacy(pharmacy, new PharmacyCandidate(pharmacy.Id, name, 1, TimeSpan.FromMinutes(5), 0),
            TimeSpan.FromMinutes(20));
    }

    private void Candidates(int inRange, params RankedPharmacy[] ranked) =>
        Selector.Setup(s => s.SelectAsync(It.IsAny<GeoPoint>(), It.IsAny<IReadOnlyCollection<(ProductId, int)>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PharmacySelection(ranked, inRange));

    [Test]
    [Property("TestCase", "TC-7.3.4")]
    public void EmptyCart_IsRejected()
    {
        Cart.Setup(c => c.GetAsync(_customer, It.IsAny<CancellationToken>())).ReturnsAsync(new Cart());

        var ex = Assert.ThrowsAsync<BusinessRuleException>(() => _service.CheckoutAsync(_customer, "key-00000001", Address, Ct));
        Assert.That(ex!.Code, Is.EqualTo(ErrorCodes.CartEmpty));
    }

    [Test]
    [Property("TestCase", "TC-7.3.5")]
    public void AddressWithoutPharmacyInRadius_IsOutOfZone_OtherwiseNoPharmacyAvailable()
    {
        Candidates(0);
        var outOfZone = Assert.ThrowsAsync<BusinessRuleException>(() => _service.CheckoutAsync(_customer, "key-00000001", Address, Ct));

        Candidates(3);
        var noPharmacy = Assert.ThrowsAsync<BusinessRuleException>(() => _service.CheckoutAsync(_customer, "key-00000002", Address, Ct));

        Assert.That((outOfZone!.Code, noPharmacy!.Code), Is.EqualTo((ErrorCodes.DeliveryOutOfZone, ErrorCodes.NoPharmacyAvailable)));
    }

    [Test]
    [Property("TestCase", "TC-7.3.6")]
    public async Task WhenBestPharmacyCannotReserve_NextOneIsUsed_AndCartIsCleared()
    {
        var first = Ranked("FIRST");
        var second = Ranked("SECOND");
        Candidates(2, first, second);
        Reservations.Setup(r => r.ReserveAsync(It.IsAny<OrderId>(), first.Pharmacy.Id, It.IsAny<IReadOnlyCollection<(ProductId, int)>>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException(ErrorCodes.StockInsufficient, "taken by a concurrent checkout"));

        var result = await _service.CheckoutAsync(_customer, "key-00000001", Address, Ct);

        Assert.Multiple(() =>
        {
            Assert.That(result.Pharmacy.Code, Is.EqualTo("SECOND"));
            Assert.That(result.Total, Is.EqualTo(12.98m));
            Assert.That(result.Status, Is.EqualTo(OrderStatus.AwaitingPayment));
        });
        Orders.Verify(o => o.Add(It.Is<Order>(x => x.PharmacyId == second.Pharmacy.Id)), Times.Once);
        Cart.Verify(c => c.DeleteAsync(_customer, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    [Property("TestCase", "TC-7.3.7")]
    public async Task RepeatedIdempotencyKey_ReturnsExistingOrderWithoutReserving()
    {
        var existing = DomainBuilders.NewOrder();
        Orders.Setup(o => o.FindByIdempotencyKeyAsync(new UserId(_customer), "key-00000001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        MockOf<IPharmacyRepository>().Setup(p => p.GetAsync(existing.PharmacyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Pharmacy.Create(existing.PharmacyId, "P", "P", "1 Test Street", "New York", "10001", new GeoPoint(40.75, -73.99),
                OpeningHours.NonStop()));
        var result = await _service.CheckoutAsync(_customer, "key-00000001", Address, Ct);

        Assert.That((result.OrderId, result.IsReplay), Is.EqualTo((existing.Id.Value, true)));
        Reservations.VerifyNoOtherCalls();
    }
}
