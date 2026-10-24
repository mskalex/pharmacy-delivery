using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Tests.Seed;

namespace PharmacyDelivery.Tests.Integration;

public sealed record ApiResponse(HttpStatusCode Status, JsonNode? Body, HttpResponseHeaders Headers)
{
    public string? Code => Body?["code"]?.GetValue<string>();

    public string Text(string path) => Node(path)!.GetValue<string>();

    public Guid Id(string path) => Guid.Parse(Text(path));

    public JsonNode? Node(string path)
    {
        var node = Body;
        foreach (var part in path.Split('.'))
            node = int.TryParse(part, CultureInfo.InvariantCulture, out var index) ? node?[index] : node?[part];

        return node;
    }
}

/// <summary>HTTP client of the test server with JSON helpers. Each client uses its own X-Forwarded-For address,
/// so rate-limit partitions of different tests never collide.</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    public ApiClient(string? forwardedFor = null)
    {
        _http = IntegrationEnvironment.Factory.CreateClient();
        _http.DefaultRequestHeaders.Add("X-Forwarded-For",
            forwardedFor ?? $"10.{Random.Shared.Next(255)}.{Random.Shared.Next(255)}.{Random.Shared.Next(1, 255)}");
    }

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public static async Task<ApiClient> LoginAsync(string email, string password = TestWorld.Password)
    {
        var client = new ApiClient();
        var response = await client.PostAsync("/api/v1/auth/login", new { email, password });
        Assert.That(response.Status, Is.EqualTo(HttpStatusCode.OK), $"login of {email}: {response.Body}");
        client.UseTokens(response);
        return client;
    }

    public void UseTokens(ApiResponse tokens)
    {
        AccessToken = tokens.Text("accessToken");
        RefreshToken = tokens.Text("refreshToken");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
    }

    public Task<ApiResponse> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);

    public Task<ApiResponse> PostAsync(string path, object? body = null, IDictionary<string, string>? headers = null) =>
        SendAsync(HttpMethod.Post, path, body, headers);

    public Task<ApiResponse> PutAsync(string path, object? body) => SendAsync(HttpMethod.Put, path, body);

    public Task<ApiResponse> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    public async Task<ApiResponse> SendRawAsync(HttpMethod method, string path, string json, IDictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (key, value) in headers ?? new Dictionary<string, string>())
            request.Headers.Add(key, value);

        using var response = await _http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new ApiResponse(response.StatusCode,
            string.IsNullOrWhiteSpace(text) || text[0] is not ('{' or '[') ? null : JsonNode.Parse(text), response.Headers);
    }

    /// <summary>Puts products into the cart and places an order; returns the checkout response.</summary>
    public async Task<ApiResponse> CheckoutAsync(IEnumerable<(Guid ProductId, int Quantity)> lines, string? idempotencyKey = null,
        object? address = null)
    {
        foreach (var (productId, quantity) in lines)
        {
            var put = await PutAsync($"/api/v1/cart/items/{productId}", new { quantity });
            Assert.That(put.Status, Is.EqualTo(HttpStatusCode.OK), put.Body?.ToJsonString());
        }

        return await PostAsync(
            "/api/v1/checkout",
            new { address = address ?? TestWorld.Address },
            new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey ?? Guid.NewGuid().ToString("N") });
    }

    /// <summary>Starts the payment and lets the fake PSP send a correctly signed "succeeded" webhook.</summary>
    public async Task PayAsync(Guid orderId)
    {
        var payment = await PostAsync($"/api/v1/orders/{orderId}/payments");
        Assert.That(payment.Status, Is.EqualTo(HttpStatusCode.Created), payment.Body?.ToJsonString());
        var webhook = await SendWebhookAsync(payment.Text("providerPaymentId"), "succeeded",
            payment.Node("amount.amount")!.GetValue<decimal>());
        Assert.That(webhook.Status, Is.EqualTo(HttpStatusCode.OK), webhook.Body?.ToJsonString());
    }

    public Task<ApiResponse> SendWebhookAsync(string providerPaymentId, string status, decimal amount, string? signature = null)
    {
        var body = JsonSerializer.Serialize(new { providerPaymentId, status, amount, currency = "USD" },
            PharmacyDeliveryJsonOptions.Tolerant);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return SendRawAsync(HttpMethod.Post, "/api/v1/webhooks/psp", body, new Dictionary<string, string>
        {
            ["X-Psp-Timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
            ["X-Psp-Signature"] = signature ?? HmacSignature.Compute(IntegrationEnvironment.WebhookSecret, timestamp, body),
        });
    }

    public void Dispose() => _http.Dispose();

    private async Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, IDictionary<string, string>? headers = null) =>
        await SendRawAsync(method, path, body is null ? string.Empty : JsonSerializer.Serialize(body, PharmacyDeliveryJsonOptions.Tolerant),
            headers);
}

public static class Eventually
{
    /// <summary>Polls until the condition holds (asynchronous flows through RabbitMQ) or fails after the timeout.</summary>
    public static async Task<T> UntilAsync<T>(Func<Task<T>> probe, Func<T, bool> condition, string description, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        T value;
        do
        {
            value = await probe();
            if (condition(value))
                return value;

            await Task.Delay(200);
        } while (DateTime.UtcNow < deadline);

        Assert.Fail($"Timed out waiting for: {description}. Last value: {value}");
        return value;
    }
}
