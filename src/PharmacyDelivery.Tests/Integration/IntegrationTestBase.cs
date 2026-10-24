using System.Net;
using PharmacyDelivery.Tests.Seed;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace PharmacyDelivery.Tests.Integration;

/// <summary>
/// Base of integration fixtures: real PostgreSQL/PostGIS, Redis and RabbitMQ in containers and the API host
/// (<see cref="IntegrationEnvironment"/>). Derived fixtures choose their data with <see cref="UseSeedAttribute"/>.
/// Run without Docker: <c>dotnet test --filter "TestCategory!=Integration"</c>.
/// </summary>
[Category("Integration")]
[NonParallelizable]
public abstract class IntegrationTestBase : TestBase
{
    private AsyncServiceScope? _scope;

    /// <summary><see cref="TestBase.Get{T}"/> and <see cref="TestBase.Service{T}"/> resolve from a scope of the real API host (own DbContext).</summary>
    protected override IServiceProvider Provider => (_scope ??= Factory.Services.CreateAsyncScope()).ServiceProvider;

    protected override bool OwnsContainer => false;

    [TearDown]
    public async Task DisposeHostScope()
    {
        if (_scope is { } scope)
            await scope.DisposeAsync();

        _scope = null;
    }

    /// <summary>The shared API host.</summary>
    protected static PharmacyDeliveryApiFactory Factory => IntegrationEnvironment.Factory;

    /// <summary>Signs in through the real login endpoint and returns an authenticated client.</summary>
    protected static Task<ApiClient> LoginAsync(string email, string password = TestWorld.Password) =>
        ApiClient.LoginAsync(email, password);

    /// <summary>Client without a token (own rate-limit partition unless <paramref name="forwardedFor"/> is given).</summary>
    protected static ApiClient Anonymous(string? forwardedFor = null) => new(forwardedFor);

    /// <summary>Runs code against services of a fresh DI scope (own DbContext), like a request would.</summary>
    protected static async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Executes SQL directly against the test database (to prepare edge cases such as expired reservations).</summary>
    protected static async Task<int> ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(IntegrationEnvironment.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Waits until the order reaches <paramref name="status"/> (asynchronous steps go through RabbitMQ).</summary>
    protected static Task<ApiResponse> WaitForStatusAsync(ApiClient client, Guid orderId, params string[] status) =>
        Eventually.UntilAsync(
            () => client.GetAsync($"/api/v1/orders/{orderId}"),
            o => status.Contains(o.Text("status")),
            $"order {orderId} in status {string.Join("/", status)}");

    /// <summary>Checkout + payment through the signed webhook; returns once the pharmacy received the order.</summary>
    protected static async Task<Guid> PaidOrderAsync(ApiClient customer, params (Guid ProductId, int Quantity)[] lines)
    {
        var checkout = await customer.CheckoutAsync(lines.Length == 0 ? [(TestWorld.Product1.Value, 1)] : lines);
        Assert.That(checkout.Status, Is.EqualTo(HttpStatusCode.Created), checkout.Body?.ToJsonString());
        var orderId = checkout.Id("orderId");
        await customer.PayAsync(orderId);
        await WaitForStatusAsync(customer, orderId, "assignedToPharmacy");
        return orderId;
    }

    /// <summary>The pharmacist accepts and packs the order (stock committed, courier dispatched).</summary>
    protected static async Task AcceptAndPackAsync(ApiClient pharmacist, Guid orderId)
    {
        Assert.That((await pharmacist.PostAsync($"/api/v1/pharmacy/orders/{orderId}/accept")).Status, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await pharmacist.PostAsync($"/api/v1/pharmacy/orders/{orderId}/ready")).Status, Is.EqualTo(HttpStatusCode.NoContent));
    }
}
