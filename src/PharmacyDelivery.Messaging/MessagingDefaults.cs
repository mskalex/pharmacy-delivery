using MassTransit;

namespace PharmacyDelivery.Messaging;

/// <summary>
/// Bus conventions shared by every endpoint. Registration lives in the composition root
/// (PharmacyDelivery.Api/DependencyInjection.cs); this class only describes the policies.
/// </summary>
public static class MessagingDefaults
{
    /// <summary>Prefix of every queue/exchange name, so the broker can be shared by several systems.</summary>
    public const string EndpointPrefix = "pharmacy-delivery";

    /// <summary>kebab-case endpoint names with the system prefix, e.g. <c>pharmacy-delivery-order-paid</c>.</summary>
    public static IEndpointNameFormatter EndpointNameFormatter { get; } = new KebabCaseEndpointNameFormatter(EndpointPrefix, false);

    /// <summary>Immediate retries for transient failures (deadlock, serialization failure, network blip).</summary>
    public static void ConfigureRetry(IRetryConfigurator retry, IReadOnlyCollection<Type> nonRetriableExceptions)
    {
        retry.Exponential(5, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200));
        foreach (var type in nonRetriableExceptions)
        {
            retry.Ignore(type);
        }
    }

    /// <summary>
    /// Second-level redelivery for longer outages (database or Redis down). After this the message lands in the
    /// <c>_error</c> queue (dead letter) for manual inspection.
    /// </summary>
    public static void ConfigureRedelivery(IRedeliveryConfigurator redelivery, IReadOnlyCollection<Type> nonRetriableExceptions)
    {
        redelivery.Intervals(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        foreach (var type in nonRetriableExceptions)
        {
            redelivery.Ignore(type);
        }
    }

    /// <summary>Maximum number of messages processed concurrently by one consumer endpoint.</summary>
    public const int ConcurrentMessageLimit = 8;

    public const ushort PrefetchCount = 16;
}
