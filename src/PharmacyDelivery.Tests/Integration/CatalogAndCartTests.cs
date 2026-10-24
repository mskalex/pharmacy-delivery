using System.Net;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Tests.Seed;

namespace PharmacyDelivery.Tests.Integration;

[TestFixture]
[UseSeed(TestSeedProfiles.Minimal)]
public sealed class CatalogAndCartTests : IntegrationTestBase
{
    [Test]
    [Property("TestCase", "TC-7.1.1")]
    public async Task Catalog_SearchesByNameFragmentAndCategory_Anonymously()
    {
        using var anonymous = Anonymous();

        var byName = await anonymous.GetAsync("/api/v1/catalog/products?search=ACETAM");
        var byCategory = await anonymous.GetAsync("/api/v1/catalog/products?category=vitaminsAndSupplements");

        Assert.Multiple(() =>
        {
            Assert.That(byName.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(byName.Node("items")!.AsArray().Select(p => p!["name"]!.GetValue<string>()),
                Is.EqualTo(new[] { "Test Acetaminophen" }));
            Assert.That(byName.Node("items.0.price.currency")!.GetValue<string>(), Is.EqualTo("USD"));
            Assert.That(byCategory.Node("items")!.AsArray().Select(p => p!["category"]!.GetValue<string>()),
                Is.All.EqualTo("vitaminsAndSupplements"));
            Assert.That(byCategory.Node("totalCount")!.GetValue<long>(), Is.EqualTo(1));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.1.2")]
    public async Task PriceChange_InvalidatesCatalogCache()
    {
        using var anonymous = Anonymous();
        using var admin = await LoginAsync(TestWorld.Admin);
        var path = $"/api/v1/catalog/products/{TestWorld.Product1.Value}";
        Assert.That((await anonymous.GetAsync(path)).Node("price.amount")!.GetValue<decimal>(), Is.EqualTo(10m), "cached now");

        var change = await admin.PutAsync($"/api/v1/admin/products/{TestWorld.Product1.Value}/price", new { price = 12.5m });

        Assert.That(change.Status, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await anonymous.GetAsync(path)).Node("price.amount")!.GetValue<decimal>(), Is.EqualTo(12.5m));
    }

    [Test]
    [Property("TestCase", "TC-7.2.1")]
    public async Task Cart_AddUpdateRemove_TotalFollowsCatalogPrices()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        await customer.PutAsync($"/api/v1/cart/items/{TestWorld.Product1.Value}", new { quantity = 2 });
        var updated = await customer.PutAsync($"/api/v1/cart/items/{TestWorld.Product2.Value}", new { quantity = 1 });
        var removed = await customer.DeleteAsync($"/api/v1/cart/items/{TestWorld.Product1.Value}");

        Assert.Multiple(() =>
        {
            Assert.That(updated.Node("total.amount")!.GetValue<decimal>(), Is.EqualTo(40m));
            Assert.That(updated.Node("lines")!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(removed.Node("total.amount")!.GetValue<decimal>(), Is.EqualTo(20m));
            Assert.That(removed.Node("expiresAt"), Is.Not.Null, "cart TTL is reported");
        });
    }

    [Test]
    [Property("TestCase", "TC-7.2.2")]
    public async Task Cart_RejectsQuantityOutOfRangeAndUnknownProduct()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        var tooMany = await customer.PutAsync($"/api/v1/cart/items/{TestWorld.Product1.Value}", new { quantity = 11 });
        var unknown = await customer.PutAsync($"/api/v1/cart/items/{Guid.NewGuid()}", new { quantity = 1 });

        Assert.Multiple(() =>
        {
            Assert.That((tooMany.Status, tooMany.Code), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed)));
            Assert.That((unknown.Status, unknown.Code), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.ProductNotFound)));
        });
    }
}
