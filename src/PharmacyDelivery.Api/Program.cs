using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Web;
using PharmacyDelivery.Api;
using PharmacyDelivery.Api.Configuration;
using PharmacyDelivery.Api.Hubs;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Infrastructure.Concrete;

// Program.cs: host, NLog bootstrap, AddPharmacyDelivery() (all DI lives in DependencyInjection.cs) and the middleware pipeline.
if (args.Contains(HealthProbe.Argument))
    return await HealthProbe.RunAsync();

var bootstrapLogger = LogManager.Setup()
    .SetupExtensions(NLogExtensions.Register)
    .LoadConfigurationFromAppSettings()
    .GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    builder.Host.UseDefaultServiceProvider(options =>
    {
        // DI mistakes (captive dependencies, missing registrations) fail at startup, not on the first request.
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    });
    builder.Services.AddPharmacyDelivery(builder.Configuration, builder.Environment, builder.Logging);

    var app = builder.Build();
    StartupReport.Log(app.Services, app.Configuration, app.Environment);

    app.UseForwardedHeaders();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    if (app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value.UseHsts)
        app.UseHsts();

    app.UseMiddleware<SecurityHeadersMiddleware>();
    if (app.Services.GetRequiredService<IOptions<SwaggerOptions>>().Value.Enabled)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            foreach (var description in app.DescribeApiVersions())
                options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", $"PharmacyDelivery API {description.GroupName}");

            options.DocumentTitle = "PharmacyDelivery API";
        });
    }

    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseCors();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.UseMiddleware<AuditMiddleware>();

    app.MapControllers();
    app.MapHub<TrackingHub>("/hubs/v1/tracking");
    app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync })
        .AllowAnonymous();
    app.MapHealthChecks("/health/ready",
            new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = HealthResponseWriter.WriteAsync })
        .AllowAnonymous();

    // Migrations and seed run before the server accepts traffic (advisory lock: one instance at a time).
    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<DataInitializer>().InitializeAsync(app.Lifetime.ApplicationStopping);

    bootstrapLogger.Info("PharmacyDelivery API {Version} starting in {Environment}", AppInfo.Version, AppInfo.Environment);
    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    bootstrapLogger.Fatal(ex, "PharmacyDelivery API terminated unexpectedly");
    throw;
}
finally
{
    LogManager.Shutdown();
}

/// <summary>Entry point; public for WebApplicationFactory in tests.</summary>
public partial class Program;
