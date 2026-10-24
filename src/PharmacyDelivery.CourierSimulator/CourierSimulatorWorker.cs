using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Geo;
using PharmacyDelivery.Common.Serialization;

namespace PharmacyDelivery.CourierSimulator;

public sealed class SimulatorOptions
{
    public const string SectionName = "Simulator";

    [Required] public string ApiBaseUrl { get; set; } = "http://localhost:5080";

    /// <summary>Password of the seeded courier accounts (Seed:UsersPassword of the API).</summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>Interval between GPS points.</summary>
    [Range(1, 60)]
    public int IntervalSeconds { get; set; } = 2;

    /// <summary>Courier speed; the demo uses an unrealistically fast courier so a delivery takes ~2 minutes.</summary>
    [Range(5, 500)]
    public double SpeedKmh { get; set; } = 90;

    /// <summary>An idle courier reports its position this often (dispatch ignores stale positions).</summary>
    [Range(5, 600)]
    public int IdleReportSeconds { get; set; } = 20;

    public List<SimulatedCourier> Couriers { get; set; } = [];
}

public sealed class SimulatedCourier
{
    public string Email { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }
}

/// <summary>
/// Drives every configured courier like the courier mobile app would: logs in, reports GPS positions, picks up the
/// assigned order at the pharmacy, drives to the customer and confirms delivery. Each courier runs as its own task.
/// </summary>
public sealed class CourierSimulatorWorker(
    IHttpClientFactory httpClientFactory,
    IOptions<SimulatorOptions> options,
    ILogger<CourierSimulatorWorker> logger) : BackgroundService
{
    public const string HttpClientName = "pharmacy-delivery-api";

    private readonly SimulatorOptions _options = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(_options.Couriers.Select(c => RunCourierAsync(c, stoppingToken)));

    private async Task RunCourierAsync(SimulatedCourier courier, CancellationToken stoppingToken)
    {
        var position = (courier.Latitude, courier.Longitude);
        var lastIdleReport = DateTimeOffset.MinValue;
        string? token = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var http = httpClientFactory.CreateClient(HttpClientName);
                token ??= await LoginAsync(http, courier.Email, stoppingToken);
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await http.GetAsync(new Uri("api/v1/courier/deliveries/current", UriKind.Relative), stoppingToken);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    token = null; // expired or revoked: log in again
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    if (DateTimeOffset.UtcNow - lastIdleReport > TimeSpan.FromSeconds(_options.IdleReportSeconds))
                    {
                        await ReportAsync(http, position, 0, stoppingToken);
                        lastIdleReport = DateTimeOffset.UtcNow;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
                    continue;
                }

                var delivery = await response.Content.ReadFromJsonAsync<DeliveryDto>(PharmacyDeliveryJsonOptions.Tolerant, stoppingToken)
                               ?? throw new InvalidOperationException("Empty delivery.");

                if (delivery.Status == "assigned")
                {
                    logger.LogInformation("{Courier}: driving to pharmacy {Pharmacy} for order {Order}", courier.Email,
                        delivery.Pharmacy.Name, delivery.OrderNumber);
                    position = await DriveAsync(http, position, (delivery.Pharmacy.Latitude, delivery.Pharmacy.Longitude), stoppingToken);
                    await PostAsync(http, $"api/v1/courier/deliveries/{delivery.DeliveryId}/pickup", stoppingToken);
                }

                logger.LogInformation("{Courier}: delivering order {Order}", courier.Email, delivery.OrderNumber);
                position = await DriveAsync(http, position, (delivery.Destination.Latitude, delivery.Destination.Longitude), stoppingToken);
                await PostAsync(http, $"api/v1/courier/deliveries/{delivery.DeliveryId}/deliver", stoppingToken);
                logger.LogInformation("{Courier}: order {Order} delivered", courier.Email, delivery.OrderNumber);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Courier}: simulation step failed; retrying", courier.Email);
                token = null;
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<(double Lat, double Lng)> DriveAsync(
        HttpClient http,
        (double Lat, double Lng) from,
        (double Lat, double Lng) to,
        CancellationToken cancellationToken)
    {
        var distanceKm = Haversine.DistanceKm(from.Lat, from.Lng, to.Lat, to.Lng);
        var stepKm = _options.SpeedKmh * _options.IntervalSeconds / 3600.0;
        var steps = Math.Max(1, (int)Math.Ceiling(distanceKm / stepKm));
        var position = from;
        for (var i = 1; i <= steps; i++)
        {
            position = Haversine.Interpolate(from.Lat, from.Lng, to.Lat, to.Lng, (double)i / steps);
            await ReportAsync(http, position, _options.SpeedKmh, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), cancellationToken);
        }

        return position;
    }

    private static async Task ReportAsync(HttpClient http, (double Lat, double Lng) position, double speed,
        CancellationToken cancellationToken)
    {
        var batch = new
        {
            Points = new[]
                { new { Latitude = position.Lat, Longitude = position.Lng, RecordedAt = DateTimeOffset.UtcNow, SpeedKmh = speed } }
        };
        using var response =
            await http.PostAsJsonAsync("api/v1/courier/locations", batch, PharmacyDeliveryJsonOptions.Tolerant, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task PostAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(new Uri(path, UriKind.Relative), null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> LoginAsync(HttpClient http, string email, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("api/v1/auth/login", new { Email = email, _options.Password },
            PharmacyDeliveryJsonOptions.Tolerant, cancellationToken);
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenDto>(PharmacyDeliveryJsonOptions.Tolerant, cancellationToken)
                     ?? throw new InvalidOperationException("Empty login response.");
        logger.LogInformation("{Courier}: logged in", email);
        return tokens.AccessToken;
    }

    private sealed record TokenDto(string AccessToken);

    private sealed record PointDto(string Name, double Latitude, double Longitude);

    private sealed record DeliveryDto(Guid DeliveryId, string OrderNumber, string Status, PointDto Pharmacy, PointDto Destination);
}
