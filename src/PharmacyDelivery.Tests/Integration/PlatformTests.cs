using System.Net;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Tests.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;

namespace PharmacyDelivery.Tests.Integration;

[TestFixture]
[UseSeed(TestSeedProfiles.Minimal)]
public sealed class PlatformTests : IntegrationTestBase
{
    [Test]
    [Property("TestCase", "TC-7.13.2")]
    public async Task Validation_UnknownFieldAndInvalidFields_Give400ProblemDetailsWithoutInternals()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        var unknownField = await customer.SendRawAsync(HttpMethod.Put, $"/api/v1/cart/items/{TestWorld.Product1.Value}",
            """{"quantity":1,"discount":50}""");
        var invalid = await customer.PostAsync("/api/v1/checkout",
            new { address = new { street = "", city = "New York", postalCode = "1001", latitude = 95, longitude = -73 } },
            new Dictionary<string, string> { ["Idempotency-Key"] = "key-validation-1" });

        Assert.Multiple(() =>
        {
            Assert.That((unknownField.Status, unknownField.Code), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed)));
            Assert.That(invalid.Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(invalid.Node("errors")!.AsObject().Select(e => e.Key),
                Is.SupersetOf(new[] { "address.street", "address.postalCode", "address.latitude" }));
            Assert.That(invalid.Node("traceId"), Is.Not.Null);
            Assert.That(invalid.Node("exception"), Is.Null, "no stack traces outside Development");
        });
    }

    [Test]
    [Property("TestCase", "TC-7.14.3")]
    public async Task ReadServices_DoNotTrackEntities()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        var orderId = (await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)])).Id("orderId");
        await using var scope = Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var user = new Mock<ICurrentUser>();
        user.SetupGet(u => u.Role).Returns(Roles.Admin);

        await services.GetRequiredService<ICatalogService>().SearchAsync("test", null, new PageRequest(), CancellationToken.None);
        await services.GetRequiredService<IOrderService>().GetDetailsAsync(orderId, user.Object, CancellationToken.None);
        await services.GetRequiredService<IOrderService>()
            .GetMyOrdersAsync(TestWorld.UserIdOf(TestWorld.Customer).Value, new PageRequest(), CancellationToken.None);

        Assert.That(services.GetRequiredService<PharmacyDeliveryDbContext>().ChangeTracker.Entries(), Is.Empty);
    }

    [TestCase(
        "SELECT id FROM pharmacies WHERE is_active AND ST_DWithin(location, ST_SetSRID(ST_MakePoint(-73.99, 40.75), 4326)::geography, 5000)",
        "ix_pharmacies_location_active")]
    [TestCase("SELECT id FROM products WHERE name ILIKE '%paracet%'", "ix_products_name_trgm")]
    [TestCase("SELECT id FROM stock_reservations WHERE status = 'Active' AND expires_at IS NOT NULL AND expires_at < now()",
        "ix_stock_reservations_expires_active")]
    [TestCase("SELECT id FROM orders WHERE customer_id = '00000000-0000-0000-0000-000000000001' ORDER BY created_at DESC LIMIT 20",
        "ix_orders_customer_created")]
    [TestCase(
        "SELECT id FROM orders WHERE pharmacy_id = '00000000-0000-0000-0000-000000000001' AND status IN ('AssignedToPharmacy','Accepted','ReadyForPickup','CourierAssigned')",
        "ix_orders_pharmacy_queue")]
    [TestCase("SELECT jti FROM access_tokens WHERE user_id = '00000000-0000-0000-0000-000000000001' AND revoked_at IS NULL",
        "ix_access_tokens_user_active")]
    [Property("TestCase", "TC-7.14.4")]
    public async Task KeyQueries_UseTheirIndexes(string sql, string index)
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.PostgresConnectionString);
        await connection.OpenAsync();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off", connection))
            await off.ExecuteNonQueryAsync();

        await using var explain = new NpgsqlCommand("EXPLAIN (FORMAT JSON) " + sql, connection);
        var plan = (string)(await explain.ExecuteScalarAsync())!;

        Assert.That(plan, Does.Contain(index));
    }

    [Test]
    [Property("TestCase", "TC-7.17.1")]
    public async Task Versioning_ReportsSupportedVersions_RejectsUnknownVersion()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        var v1 = await customer.GetAsync("/api/v1/catalog/products");
        var v9 = await customer.GetAsync("/api/v9/catalog/products");

        Assert.That(v1.Headers.GetValues("api-supported-versions").Single(), Does.Contain("1.0"));
        // URL-segment versioning: an unknown version matches no route -> 404 ProblemDetails.
        Assert.That((v9.Status, v9.Code), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)), v9.Body?.ToJsonString());
    }

    [Test]
    [Property("TestCase", "TC-7.17.2")]
    public async Task Health_ReportsVersionEnvironmentAndDependencies()
    {
        using var anonymous = Anonymous();

        var ready = await anonymous.GetAsync("/health/ready");

        Assert.Multiple(() =>
        {
            Assert.That(ready.Text("status"), Is.EqualTo("Healthy"));
            Assert.That(ready.Text("environment"), Is.EqualTo("IntegrationTests"));
            Assert.That(ready.Node("checks")!.AsObject().Select(c => c.Key), Is.SupersetOf(new[] { "postgres", "redis" }));
            Assert.That(ready.Text("version"), Is.Not.Empty);
        });
    }

    [Test]
    [Property("TestCase", "TC-7.19.4")]
    public async Task AdminSeedReset_ReplacesAllDataWithTheTemplate()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)]);
        using var admin = await LoginAsync(TestWorld.Admin);

        var reset = await admin.PostAsync("/api/v1/admin/maintenance/seed/reset", new { profile = "minimal" });

        Assert.That(reset.Status, Is.EqualTo(HttpStatusCode.OK), reset.Body?.ToJsonString());
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDeliveryDbContext>();
        Assert.That((await db.Orders.CountAsync(), await db.Products.CountAsync()), Is.EqualTo((0, 3)));
    }

    [Test]
    [Property("TestCase", "TC-7.20.1")]
    public async Task Checkout_ProducesTraceWithBusinessSpans_TraceIdMatchesResponse()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        IntegrationEnvironment.Spans.Clear();

        var checkout = await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)]);

        var traceId = checkout.Headers.GetValues("X-Trace-Id").Single();
        var spans = await Eventually.UntilAsync(
            () => Task.FromResult(IntegrationEnvironment.Spans.ToList().Where(s => s.TraceId.ToString() == traceId).ToList()),
            s => s.Any(a => a.DisplayName == "stock.reserve"),
            "spans exported");
        var root = spans.Single(s => s.DisplayName == "checkout");
        Assert.That(spans.Single(s => s.DisplayName == "stock.reserve").ParentSpanId, Is.EqualTo(root.SpanId));
        Assert.That(root.GetTagItem("result"), Is.EqualTo("created"));
    }

    [Test]
    [Property("TestCase", "TC-7.24.2")]
    public async Task AuditedAction_RecordsOldAndNewValues_VisibleToAdmin()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        var orderId = (await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)])).Id("orderId");

        var cancel = await customer.PostAsync($"/api/v1/orders/{orderId}/cancel", new { reason = "Ordered by mistake" });
        Assert.That(cancel.Status, Is.EqualTo(HttpStatusCode.OK));

        using var admin = await LoginAsync(TestWorld.Admin);
        var audit = await admin.GetAsync($"/api/v1/admin/audit?entityType=Order&entityId={orderId}");
        var entry = audit.Node("items")!.AsArray().Single(e => e!["action"]!.GetValue<string>() == "order.cancel")!;
        var status = entry["changes"]!.AsArray()
            .Single(c => c!["entityType"]!.GetValue<string>() == "Order" && c["property"]!.GetValue<string>() == "Status")!;

        Assert.Multiple(() =>
        {
            Assert.That((status["oldValue"]!.GetValue<string>(), status["newValue"]!.GetValue<string>()),
                Is.EqualTo(("AwaitingPayment", "Cancelled")));
            Assert.That(entry["userId"]!.GetValue<Guid>(), Is.EqualTo(TestWorld.UserIdOf(TestWorld.Customer).Value));
            Assert.That(entry["traceId"]!.GetValue<string>(), Has.Length.EqualTo(32));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.24.3")]
    public async Task Audit_MasksSecrets_AndFailedRequestsLeaveNoEntry()
    {
        using var anonymous = Anonymous();
        await anonymous.PostAsync("/api/v1/auth/register",
            new { email = "audit@test.local", password = "Long-Secure-Password-1", displayName = "Audit" });
        using var customer = await LoginAsync(TestWorld.Customer);
        var orderId = (await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)])).Id("orderId");
        await customer.PostAsync($"/api/v1/orders/{orderId}/cancel", new { reason = "first" });
        var secondCancel = await customer.PostAsync($"/api/v1/orders/{orderId}/cancel", new { reason = "second" });

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDeliveryDbContext>();
        var passwordValues = await db.AuditChanges.AsNoTracking().Where(c => c.Property == "PasswordHash").Select(c => c.NewValue)
            .ToListAsync();
        var cancelEntries = await db.AuditEntries.AsNoTracking().CountAsync(e => e.Action == "order.cancel");

        Assert.Multiple(() =>
        {
            Assert.That(passwordValues, Is.Not.Empty.And.All.EqualTo("***"));
            Assert.That(secondCancel.Status, Is.EqualTo((HttpStatusCode)422));
            Assert.That(cancelEntries, Is.EqualTo(1), "the rejected second cancel was rolled back together with its audit entry");
        });
    }
}
