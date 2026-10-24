using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Services;

namespace PharmacyDelivery.Tests.Domain;

[TestFixture]
public sealed class StockTests : TestBase
{
    private static StockItem Stock(int onHand) => StockItem.Create(PharmacyId.New(), ProductId.New(), onHand, DomainBuilders.Now);

    [Test]
    [Property("TestCase", "TC-7.4.5")]
    public void ReserveCommitRelease_KeepInvariant()
    {
        var stock = Stock(10);

        stock.Reserve(4, DomainBuilders.Now);
        stock.Commit(3, DomainBuilders.Now);
        stock.Release(1, DomainBuilders.Now);

        Assert.Multiple(() =>
        {
            Assert.That(stock.OnHand, Is.EqualTo(7));
            Assert.That(stock.Reserved, Is.Zero);
            Assert.That(stock.Available, Is.EqualTo(7));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.4.6")]
    public void Reserve_MoreThanAvailable_ThrowsStockInsufficient()
    {
        var stock = Stock(2);

        var ex = Assert.Throws<DomainException>(() => stock.Reserve(3, DomainBuilders.Now));
        Assert.That(ex!.Code, Is.EqualTo(ErrorCodes.StockInsufficient));
        Assert.That(stock.Reserved, Is.Zero);
    }

    [Test]
    public void ApplyStockCount_BelowReserved_KeepsInvariantAndReportsDeficit()
    {
        var stock = Stock(10);
        stock.Reserve(6, DomainBuilders.Now);

        var deficit = stock.ApplyStockCount(4, DomainBuilders.Now);

        Assert.That((deficit, stock.OnHand, stock.Reserved), Is.EqualTo((2, 6, 6)));
    }

    [Test]
    [Property("TestCase", "TC-7.4.7")]
    public void StockAllocator_IsAllOrNothing()
    {
        var a = Stock(5);
        var b = Stock(1);

        Assert.Throws<DomainException>(() => StockAllocator.ReserveAll([a, b], [(a.ProductId, 2), (b.ProductId, 2)], DomainBuilders.Now));
        Assert.That((a.Reserved, b.Reserved), Is.EqualTo((0, 0)), "nothing may be reserved when one line fails");

        StockAllocator.ReserveAll([a, b], [(a.ProductId, 2), (b.ProductId, 1)], DomainBuilders.Now);
        Assert.That((a.Reserved, b.Reserved), Is.EqualTo((2, 1)));
    }
}
