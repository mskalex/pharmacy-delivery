using Microsoft.Extensions.Options;
using NLog.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PharmacyDelivery.CourierSimulator;

/// <summary>All DI of the courier simulator host (separate deployable, separate single DI file).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCourierSimulator(this IServiceCollection services, IConfiguration configuration,
        ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddNLog(configuration);

        services.AddOptions<SimulatorOptions>()
            .Bind(configuration.GetSection(SimulatorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient(CourierSimulatorWorker.HttpClientName, (sp, client) =>
        {
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<SimulatorOptions>>().Value.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHostedService<CourierSimulatorWorker>();

        var otlp = configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlp))
        {
            // The simulator's HTTP calls start the traces "courier app -> API -> Redis -> SignalR".
            services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService("pharmacy-delivery-courier-simulator"))
                .WithTracing(t => t.AddHttpClientInstrumentation().AddOtlpExporter(o => o.Endpoint = new Uri(otlp)));
        }

        return services;
    }
}
