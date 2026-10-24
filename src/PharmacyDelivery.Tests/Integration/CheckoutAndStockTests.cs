using System.Globalization;
using System.Net;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Tests.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace PharmacyDelivery.Tests.Integration;

[TestFixture]
[UseSeed(TestSeedProfiles.Minimal)]
public sealed class CheckoutAndStockTests : IntegrationTestBase
{
    private static Task<(int OnHand, int Reserved)> StockAsync(Guid productId, Guid pharmacyId) =>
        InScopeAsync(async sp =>
        {
            var item = await sp.GetRequiredService<PharmacyDeliveryDbContext>().StockItems.AsNoTracking()
                .SingleAsync(s => s.ProductId == new ProductId(productId) && s.PharmacyId == new PharmacyId(pharmacyId));
            return (item.OnHand, item.Reserved);
        });

    [Test]
    [Property("TestCase", "TC-7.3.8")]
    public async Task Checkout_ReturnsFulfillingPharmacyAndEta_AndOrderShowsIt()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        var checkout = await customer.CheckoutAsync([(TestWorld.Product1.Value, 2), (TestWorld.Product2.Value, 1)]);

        Assert.That(checkout.Status, Is.EqualTo(HttpStatusCode.Created), checkout.Body?.ToJsonString());
        Assert.Multiple(() =>
        {
            Assert.That(checkout.Text("pharmacy.name"), Is.EqualTo("Test Pharmacy 1"));
            Assert.That(checkout.Text("status"), Is.EqualTo("awaitingPayment"));
            Assert.That(checkout.Node("total.amount")!.GetValue<decimal>(), Is.EqualTo(40m));
            Assert.That(DateTimeOffset.Parse(checkout.Text("estimatedDeliveryAt"), CultureInfo.InvariantCulture),
                Is.GreaterThan(DateTimeOffset.UtcNow));
        });

        var order = await customer.GetAsync($"/api/v1/orders/{checkout.Id("orderId")}");
        Assert.That(order.Text("pharmacy.street"), Is.EqualTo("1250 Broadway"));
        Assert.That(await StockAsync(TestWorld.Product1.Value, TestWorld.Pharmacy1.Value), Is.EqualTo((50, 2)));
    }

    [Test]
    [Property("TestCase", "TC-7.3.9")]
    public async Task Checkout_WithSameIdempotencyKey_ReturnsTheSameOrder()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        var first = await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)], idempotencyKey: "same-key-0001");
        var second = await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)], idempotencyKey: "same-key-0001");

        Assert.That((first.Status, second.Status), Is.EqualTo((HttpStatusCode.Created, HttpStatusCode.OK)));
        Assert.That(second.Id("orderId"), Is.EqualTo(first.Id("orderId")));
        Assert.That(await StockAsync(TestWorld.Product1.Value, TestWorld.Pharmacy1.Value), Is.EqualTo((50, 1)), "reserved once");
    }

    [Test]
    [Property("TestCase", "TC-7.3.10")]
    public async Task Checkout_OutsideDeliveryZone_Is422()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        var response = await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)],
            address: new
            {
                street = "1400 John F Kennedy Blvd", city = "Philadelphia", postalCode = "19107", latitude = 39.9526, longitude = -75.1652
            });

        Assert.That((response.Status, response.Code), Is.EqualTo(((HttpStatusCode)422, ErrorCodes.DeliveryOutOfZone)));
    }

    [Test]
    [Property("TestCase", "TC-7.3.11")]
    public async Task Checkout_WithoutIdempotencyKey_Is400()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        await customer.PutAsync($"/api/v1/cart/items/{TestWorld.Product1.Value}", new { quantity = 1 });

        var response = await customer.PostAsync("/api/v1/checkout", new { address = TestWorld.Address });

        Assert.That((response.Status, response.Code), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed)));
    }

    [Test]
    [UseSeed(TestSeedProfiles.Concurrency)]
    [Property("TestCase", "TC-7.4.1")]
    public async Task ParallelCheckouts_NeverOversellScarceStock()
    {
        var customers = await Task.WhenAll(Enumerable.Range(1, 50).Select(i => LoginAsync(TestWorld.ConcurrencyCustomer(i))));
        foreach (var customer in customers)
            await customer.PutAsync($"/api/v1/cart/items/{TestWorld.ScarceProduct.Value}", new { quantity = 1 });

        var results = await Task.WhenAll(customers.Select(c => c.PostAsync(
            "/api/v1/checkout",
            new { address = TestWorld.Address },
            new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString("N") })));

        var created = results.Count(r => r.Status == HttpStatusCode.Created);
        var rejected = results.Where(r => r.Status != HttpStatusCode.Created).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(created, Is.EqualTo(TestWorld.ScarceStock));
            Assert.That(rejected.Select(r => r.Code).Distinct(), Is.SubsetOf(new[] { ErrorCodes.StockInsufficient, ErrorCodes.StockBusy }));
            Assert.That(results.Any(r => (int)r.Status >= 500), Is.False);
        });
        Assert.That(await StockAsync(TestWorld.ScarceProduct.Value, TestWorld.Pharmacy1.Value),
            Is.EqualTo((TestWorld.ScarceStock, TestWorld.ScarceStock)));

        foreach (var customer in customers)
            customer.Dispose();
    }

    [Test]
    [Property("TestCase", "TC-7.4.2")]
    public async Task ParallelCheckouts_WithOverlappingBaskets_DoNotDeadlock()
    {
        using var a = await LoginAsync(TestWorld.Customer);
        using var b = await LoginAsync(TestWorld.OtherCustomer);

        // Opposite insertion order of the same products: locks are still taken in product id order.
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => i % 2 == 0
            ? a.CheckoutAsync([(TestWorld.Product1.Value, 1), (TestWorld.Product2.Value, 1)])
            : b.CheckoutAsync([(TestWorld.Product2.Value, 1), (TestWorld.Product1.Value, 1)])));

        Assert.That(
            results.Where(r => r.Status != HttpStatusCode.Created && r.Status != HttpStatusCode.OK).Select(r => r.Body?.ToJsonString()),
            Is.Empty);
    }

    [Test]
    [Property("TestCase", "TC-7.4.3")]
    public async Task Checkout_WhenStockRowIsLockedLongerThanLockTimeout_Returns409StockBusy()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        await customer.PutAsync($"/api/v1/cart/items/{TestWorld.Product3.Value}", new { quantity = 1 });

        await using var connection = new NpgsqlConnection(IntegrationEnvironment.PostgresConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var lockCommand =
                     new NpgsqlCommand("SELECT 1 FROM stock_items WHERE product_id = @p FOR UPDATE", connection, transaction))
        {
            lockCommand.Parameters.AddWithValue("p", TestWorld.Product3.Value);
            await lockCommand.ExecuteNonQueryAsync();
        }

        var response = await customer.PostAsync("/api/v1/checkout", new { address = TestWorld.Address },
            new Dictionary<string, string> { ["Idempotency-Key"] = Guid.NewGuid().ToString("N") });
        await transaction.RollbackAsync();

        Assert.That((response.Status, response.Code), Is.EqualTo((HttpStatusCode.Conflict, ErrorCodes.StockBusy)));
    }

    [Test]
    [Property("TestCase", "TC-7.4.4")]
    public async Task ExpiredUnpaidReservation_IsReleased_AndOrderCancelled()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        var checkout = await customer.CheckoutAsync([(TestWorld.Product2.Value, 3)]);
        await ExecuteSqlAsync("UPDATE stock_reservations SET expires_at = now() - interval '1 minute'");

        var expired = await InScopeAsync(sp => sp.GetRequiredService<IStockReservationService>().ExpireAsync(10, CancellationToken.None));
        Assert.That(expired, Is.EqualTo(1));

        var order = await customer.GetAsync($"/api/v1/orders/{checkout.Id("orderId")}");
        Assert.That(order.Text("status"), Is.EqualTo("cancelled"));
        Assert.That(await StockAsync(TestWorld.Product2.Value, TestWorld.Pharmacy1.Value), Is.EqualTo((50, 0)));
    }
}
