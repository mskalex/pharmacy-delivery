using System.Net;
using System.Text.Json;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Tests.Seed;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace PharmacyDelivery.Tests.Integration;

[TestFixture]
[UseSeed(TestSeedProfiles.Minimal)]
public sealed class OrderLifecycleTests : IntegrationTestBase
{
    [Test]
    [Property("TestCase", "TC-7.5.6")]
    public async Task Payment_ThroughWebhookAndBus_AssignsOrderToPharmacy()
    {
        using var customer = await LoginAsync(TestWorld.Customer);

        var orderId = await PaidOrderAsync(customer);

        var order = await customer.GetAsync($"/api/v1/orders/{orderId}");
        Assert.That(order.Node("history")!.AsArray().Select(h => h!["to"]!.GetValue<string>()),
            Is.EqualTo(new[] { "awaitingPayment", "paid", "assignedToPharmacy" }));
    }

    [Test]
    [Property("TestCase", "TC-7.5.7")]
    public async Task Webhook_WithInvalidSignature_Is401()
    {
        using var anonymous = Anonymous();

        var response = await anonymous.SendWebhookAsync("fake_unknown", "succeeded", 10, signature: "00ff");

        Assert.That((response.Status, response.Code), Is.EqualTo((HttpStatusCode.Unauthorized, ErrorCodes.PaymentInvalidSignature)));
    }

    [Test]
    [Property("TestCase", "TC-7.5.8")]
    public async Task Webhook_DeliveredTwice_IsProcessedOnce()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        var checkout = await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)]);
        var payment = await customer.PostAsync($"/api/v1/orders/{checkout.Id("orderId")}/payments");
        var providerId = payment.Text("providerPaymentId");

        var results = await Task.WhenAll(customer.SendWebhookAsync(providerId, "succeeded", 10),
            customer.SendWebhookAsync(providerId, "succeeded", 10));

        Assert.That(results.Select(r => r.Status), Is.All.EqualTo(HttpStatusCode.OK));
        Assert.That(results.Select(r => r.Text("outcome")), Is.EquivalentTo(new[] { "paid", "duplicate" }));
    }

    [Test]
    [UseSeed(TestSeedProfiles.Reassign)]
    [Property("TestCase", "TC-7.6.2")]
    public async Task PharmacyRejects_OrderMovesToNextPharmacy()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        using var pharmacist1 = await LoginAsync(TestWorld.Pharmacist1);
        var orderId = await PaidOrderAsync(customer, (TestWorld.Product1.Value, 1));

        var reject = await pharmacist1.PostAsync($"/api/v1/pharmacy/orders/{orderId}/reject", new { reason = "No pharmacist on duty" });

        Assert.That(reject.Status, Is.EqualTo(HttpStatusCode.NoContent), reject.Body?.ToJsonString());
        var order = await customer.GetAsync($"/api/v1/orders/{orderId}");
        Assert.That((order.Text("status"), order.Text("pharmacy.code")), Is.EqualTo(("assignedToPharmacy", "T-2")));

        using var pharmacist2 = await LoginAsync(TestWorld.Pharmacist2);
        var queue = await pharmacist2.GetAsync("/api/v1/pharmacy/orders");
        Assert.That(queue.Body!.AsArray().Select(o => o!["id"]!.GetValue<Guid>()), Does.Contain(orderId));
    }

    [Test]
    [UseSeed(TestSeedProfiles.Reassign)]
    [Property("TestCase", "TC-7.6.3")]
    public async Task OtherPharmacy_CannotSeeOrAcceptTheOrder()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        using var pharmacist2 = await LoginAsync(TestWorld.Pharmacist2);
        var orderId = await PaidOrderAsync(customer, (TestWorld.Product1.Value, 1));

        var accept = await pharmacist2.PostAsync($"/api/v1/pharmacy/orders/{orderId}/accept");
        var details = await pharmacist2.GetAsync($"/api/v1/orders/{orderId}");

        Assert.That((accept.Status, details.Status), Is.EqualTo((HttpStatusCode.NotFound, HttpStatusCode.NotFound)));
    }

    [Test]
    [UseSeed(TestSeedProfiles.Reassign)]
    [Property("TestCase", "TC-7.6.4")]
    public async Task AcceptanceTimeout_ReassignsOrder()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        var orderId = await PaidOrderAsync(customer, (TestWorld.Product1.Value, 1));
        await ExecuteSqlAsync("UPDATE orders SET assigned_at = now() - interval '10 minutes' WHERE id = @id", ("id", orderId));

        var reassigned = await InScopeAsync(sp =>
            sp.GetRequiredService<IPharmacyOrderService>().ProcessAcceptanceTimeoutsAsync(10, CancellationToken.None));
        Assert.That(reassigned, Is.EqualTo(1));

        var order = await customer.GetAsync($"/api/v1/orders/{orderId}");
        Assert.That(order.Text("pharmacy.code"), Is.EqualTo("T-2"));
    }

    [Test]
    [UseSeed(TestSeedProfiles.Dispatch)]
    [Property("TestCase", "TC-7.7.3")]
    public async Task ReadyOrder_IsAssignedToNearestAvailableCourier_StalePositionsIgnored()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        using var pharmacist = await LoginAsync(TestWorld.Pharmacist1);
        var orderId = await PaidOrderAsync(customer);

        await AcceptAndPackAsync(pharmacist, orderId);

        var order = await WaitForStatusAsync(customer, orderId, "courierAssigned");
        Assert.That(order.Text("courierName"), Is.EqualTo("Near Courier"));
    }

    [Test]
    [Property("TestCase", "TC-7.7.4")]
    public async Task Database_RejectsSecondActiveDeliveryOfCourier()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDeliveryDbContext>();
        var courier = TestWorld.CourierIdOf(TestWorld.Courier).Value;
        const string insert = "INSERT INTO deliveries (id, order_id, courier_id, pharmacy_id, status, assigned_at) "
                              + "SELECT gen_random_uuid(), o.id, @courier, o.pharmacy_id, 'Assigned', now() FROM orders o WHERE o.id = @order";
        using var customer = await LoginAsync(TestWorld.Customer);
        var first = (await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)])).Id("orderId");
        var second = (await customer.CheckoutAsync([(TestWorld.Product1.Value, 1)])).Id("orderId");

        await db.Database.ExecuteSqlRawAsync(insert, new NpgsqlParameter("courier", courier), new NpgsqlParameter("order", first));
        var ex = Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync(insert, new NpgsqlParameter("courier", courier), new NpgsqlParameter("order", second)));

        Assert.That((ex!.SqlState, ex.ConstraintName), Is.EqualTo(("23505", "ux_deliveries_active_courier")));
    }

    [Test]
    [Property("TestCase", "TC-7.8.5")]
    public async Task Tracking_ShowsCourierOnlyInDelivery_AndOnlyToTheCustomer()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        using var other = await LoginAsync(TestWorld.OtherCustomer);
        using var pharmacist = await LoginAsync(TestWorld.Pharmacist1);
        using var courier = await LoginAsync(TestWorld.Courier);
        var orderId = await PaidOrderAsync(customer);
        await AcceptAndPackAsync(pharmacist, orderId);
        var current = await Eventually.UntilAsync(() => courier.GetAsync("/api/v1/courier/deliveries/current"),
            r => r.Status == HttpStatusCode.OK, "delivery assigned");

        var beforePickup = await customer.GetAsync($"/api/v1/orders/{orderId}/tracking");
        Assert.That(beforePickup.Node("courier"), Is.Null, "position hidden before InDelivery");

        await courier.PostAsync($"/api/v1/courier/deliveries/{current.Id("deliveryId")}/pickup");
        var ingest = await courier.PostAsync("/api/v1/courier/locations",
            new
            {
                points = new[] { new { latitude = 40.74795, longitude = -73.98775, recordedAt = DateTimeOffset.UtcNow, speedKmh = 20 } }
            });
        Assert.That(ingest.Status, Is.EqualTo(HttpStatusCode.Accepted));

        var tracking = await customer.GetAsync($"/api/v1/orders/{orderId}/tracking");
        Assert.That(tracking.Node("courier.latitude")!.GetValue<double>(), Is.EqualTo(40.74795).Within(1e-6));
        Assert.That((await other.GetAsync($"/api/v1/orders/{orderId}/tracking")).Status, Is.EqualTo(HttpStatusCode.NotFound));

        await courier.PostAsync($"/api/v1/courier/deliveries/{current.Id("deliveryId")}/deliver");
        Assert.That((await customer.GetAsync($"/api/v1/orders/{orderId}")).Text("status"), Is.EqualTo("delivered"));
    }

    [Test]
    [Property("TestCase", "TC-7.8.6")]
    public async Task SignalR_PushesCourierPositionToSubscribedCustomer()
    {
        using var customer = await LoginAsync(TestWorld.Customer);
        using var pharmacist = await LoginAsync(TestWorld.Pharmacist1);
        using var courier = await LoginAsync(TestWorld.Courier);
        var orderId = await PaidOrderAsync(customer);
        await AcceptAndPackAsync(pharmacist, orderId);
        var current = await Eventually.UntilAsync(() => courier.GetAsync("/api/v1/courier/deliveries/current"),
            r => r.Status == HttpStatusCode.OK, "delivery assigned");
        await courier.PostAsync($"/api/v1/courier/deliveries/{current.Id("deliveryId")}/pickup");

        var server = Factory.Server;
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/v1/tracking"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult(customer.AccessToken);
            })
            .Build();
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<JsonElement>("courierPosition", message => received.TrySetResult(message));
        await hub.StartAsync();
        await hub.InvokeAsync<JsonElement>("SubscribeToOrder", orderId);

        await courier.PostAsync("/api/v1/courier/locations",
            new
            {
                points = new[] { new { latitude = 40.74793, longitude = -73.98778, recordedAt = DateTimeOffset.UtcNow, speedKmh = 20 } }
            });
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.That(message.GetProperty("orderId").GetGuid(), Is.EqualTo(orderId));
        Assert.That(message.GetProperty("latitude").GetDouble(), Is.EqualTo(40.74793).Within(1e-6));
    }
}
