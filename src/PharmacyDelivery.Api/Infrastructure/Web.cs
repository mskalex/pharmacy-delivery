using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Infrastructure;

/// <summary>Application version (SemVer from git tags via MinVer) and environment, shown in health, Swagger and logs.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static string Environment { get; set; } = "Unknown";
}

/// <summary>Principal of the current HTTP request, read from JWT claims.</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private PrincipalCurrentUser Current => new(accessor.HttpContext?.User);

    public bool IsAuthenticated => Current.IsAuthenticated;

    public Guid UserId => Current.UserId;

    public string? Role => Current.Role;

    public Guid? PharmacyId => Current.PharmacyId;

    public Guid? CourierId => Current.CourierId;

    public Guid? SessionId => Current.SessionId;
}

/// <summary>
/// Current user built from a principal. SignalR hubs use it with <c>Context.User</c>: IHttpContextAccessor is not
/// reliable inside hub methods (with long polling every invocation arrives in a different HTTP request).
/// </summary>
public sealed class PrincipalCurrentUser(ClaimsPrincipal? user) : ICurrentUser
{
    private ClaimsPrincipal? User => user;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(User?.FindFirstValue(ClaimNames.Subject), out var id)
        ? id
        : throw new InvalidOperationException("No authenticated user.");

    public string? Role => User?.FindFirstValue(ClaimNames.Role);

    public Guid? PharmacyId => ParseGuid(ClaimNames.PharmacyId);

    public Guid? CourierId => ParseGuid(ClaimNames.CourierId);

    public Guid? SessionId => ParseGuid(ClaimNames.SessionId);

    private Guid? ParseGuid(string claim) => Guid.TryParse(User?.FindFirstValue(claim), out var value) ? value : null;
}

/// <summary>
/// Security headers. API responses forbid everything (they are JSON); the demo page gets a CSP that allows only
/// the pinned CDN libraries and OpenStreetMap tiles. Swagger UI is excluded (it needs inline scripts).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ApiCsp = "default-src 'none'; frame-ancestors 'none'";

    private const string PageCsp =
        "default-src 'self'; script-src 'self' https://cdnjs.cloudflare.com; style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; "
        + "img-src 'self' data: https://tile.openstreetmap.org https://cdnjs.cloudflare.com; connect-src 'self' ws: wss:; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";
        if (Activity.Current is { } activity)
            headers["X-Trace-Id"] = activity.TraceId.ToString();

        var path = context.Request.Path;
        if (path.StartsWithSegments("/swagger"))
            return next(context);

        var isApi = path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/health");
        headers.ContentSecurityPolicy = isApi ? ApiCsp : PageCsp;

        // Pages send only their origin cross-site: the OpenStreetMap tile policy rejects tile requests without a Referer.
        headers["Referrer-Policy"] = isApi ? "no-referrer" : "strict-origin-when-cross-origin";
        return next(context);
    }
}

/// <summary>JSON health report with application version and environment.</summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            version = AppInfo.Version,
            environment = AppInfo.Environment,
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(), durationMs = e.Value.Duration.TotalMilliseconds, error = e.Value.Exception?.Message
                }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, PharmacyDeliveryJsonOptions.Tolerant));
    }
}

/// <summary>
/// <c>dotnet PharmacyDelivery.Api.dll --healthcheck</c>: used by the Docker HEALTHCHECK (chiseled images have no shell, curl or
/// wget). Calls /health/live on the local port and returns 0 when healthy.
/// </summary>
public static class HealthProbe
{
    public const string Argument = "--healthcheck";

    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/health/live"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
