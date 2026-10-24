using System.Reflection;
using PharmacyDelivery.Api.Configuration;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Options;
using PharmacyDelivery.Services.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace PharmacyDelivery.Tests;

/// <summary>
/// Base of every test fixture. Each test gets its own DI container with:
/// <list type="bullet">
/// <item>settings — <see cref="Settings"/> (configuration keys, e.g. <c>"Checkout:DeliveryRadiusKm"</c>) bound to every
/// options class of the application exactly like in <c>DependencyInjection.cs</c>; unset keys keep the class defaults;</item>
/// <item>mocks — <see cref="MockOf{T}"/>; every interface the resolved service needs and nobody registered is mocked automatically,
/// and <see cref="MockOf{T}"/> returns that same mock for setups and verification;</item>
/// <item>a fixed clock (<see cref="Clock"/>, <see cref="Now"/>), an inline unit of work (<see cref="UnitOfWork"/>) and
/// captured logs (<see cref="Logs"/>);</item>
/// <item>the code under test — <see cref="Service{T}"/> builds the class with real dependencies from the container,
/// <see cref="Get{T}"/> resolves anything registered.</item>
/// </list>
/// Order inside a test: <see cref="Settings"/> / <see cref="Services"/> / <see cref="MockOf{T}"/> → <see cref="Service{T}"/> or
/// <see cref="Get{T}"/> (builds the container) → setups and verification on the mocks.
/// Integration fixtures (<see cref="Integration.IntegrationTestBase"/>) resolve the same way from the real API host.
/// </summary>
public abstract class TestBase
{
    /// <summary>Fixed moment used by tests: Wednesday 2026-10-21 10:00 UTC (06:00 in New York).</summary>
    protected static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    // Options classes of all application layers (each has a public const SectionName), bound like in DependencyInjection.cs.
    private static readonly Lazy<IReadOnlyList<(Type Type, string Section)>> OptionsTypes = new(() =>
        new[] { typeof(CheckoutOptions), typeof(DatabaseOptions), typeof(SeedOptions), typeof(AdminOptions) }
            .Select(t => t.Assembly)
            .SelectMany(a => a.GetExportedTypes())
            .Select(t => (Type: t, Field: t.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)))
            .Where(x => x.Field is { IsLiteral: true } && x.Field.FieldType == typeof(string) &&
                        x.Type is { IsClass: true, IsAbstract: false })
            .Select(x => (x.Type, (string)x.Field!.GetRawConstantValue()!))
            .ToList());

    private static readonly MethodInfo ConfigureOptions = typeof(OptionsConfigurationServiceCollectionExtensions).GetMethods()
        .Single(m => m.Name == nameof(OptionsConfigurationServiceCollectionExtensions.Configure)
                     && m.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(IServiceCollection), typeof(IConfiguration)]));

    private readonly Dictionary<Type, Mock> _mocks = [];
    private ServiceCollection _services = [];
    private IServiceProvider? _provider;

    /// <summary>Configuration of the current test (keys as in appsettings, <c>Section:Key</c>). Set before resolving services.</summary>
    protected Dictionary<string, string?> Settings { get; private set; } = [];

    /// <summary>Registrations of the current test (fakes, real implementations). Frozen once the container is built.</summary>
    protected IServiceCollection Services =>
        !OwnsContainer
            ? throw new InvalidOperationException(
                "Integration tests use the services of the API host; there is nothing to register or mock.")
            : _provider is null
                ? _services
                : throw new InvalidOperationException("The container is already built: register services before Service<T>() / Get<T>().");

    /// <summary>The clock of the current test (registered as <see cref="IClock"/>), starts at <see cref="Now"/>.</summary>
    protected FixedClock Clock { get; private set; } = new(Now);

    /// <summary>The unit of work of the current test (registered as <see cref="IUnitOfWork"/>); counts saves.</summary>
    protected InlineUnitOfWork UnitOfWork { get; private set; } = new();

    /// <summary>Logs written through <see cref="ILogger{T}"/> by the code under test.</summary>
    protected TestLog Logs { get; private set; } = new();

    /// <summary>Cancellation token of the current test (cancelled on <c>[CancelAfter]</c> timeout).</summary>
    protected static CancellationToken Ct => TestContext.CurrentContext.CancellationToken;

    [SetUp]
    public void ResetTestContainer()
    {
        _provider = null;
        _mocks.Clear();
        _services = [];
        Settings = [];
        Clock = new FixedClock(Now);
        UnitOfWork = new InlineUnitOfWork();
        Logs = new TestLog();
    }

    [TearDown]
    public async Task DisposeTestContainer()
    {
        await DisposeProviderAsync(_provider);
        Logs.Dispose();
        _provider = null;
    }

    /// <summary>
    /// Returns the mock of <typeparamref name="T"/> registered in the container — created on first use, or the one created
    /// automatically for a dependency of a resolved service.
    /// </summary>
    protected Mock<T> MockOf<T>()
        where T : class
    {
        if (_mocks.TryGetValue(typeof(T), out var existing))
            return (Mock<T>)existing;

        _ = Services; // throws once the container is built: a new mock could not reach the services anymore
        return (Mock<T>)AddMock(typeof(T));
    }

    /// <summary>
    /// The class under test: registered as itself (singleton within the test) and built by the container; dependencies that are
    /// interfaces and not registered become mocks, concrete PharmacyDelivery classes are built the same way.
    /// </summary>
    protected T Service<T>()
        where T : class
    {
        if (OwnsContainer && _provider is null)
            RegisterWithDependencies(typeof(T));

        return Get<T>();
    }

    /// <summary>Resolves <typeparamref name="T"/> from the container of the current test (builds it on first call).</summary>
    protected T Get<T>()
        where T : notnull => Provider.GetRequiredService<T>();

    /// <summary>Bound value of an options class (defaults + <see cref="Settings"/>).</summary>
    protected T OptionsValue<T>()
        where T : class => Get<IOptions<T>>().Value;

    /// <summary>Wraps an options instance the way DI would (for code built by hand).</summary>
    protected static IOptions<T> OptionsOf<T>(T value)
        where T : class => Options.Create(value);

    /// <summary>A separate clock frozen at <paramref name="now"/> (for code built by hand).</summary>
    protected static FixedClock ClockAt(DateTimeOffset? now = null) => new(now ?? Now);

    /// <summary>The service provider of the current test; integration fixtures return a scope of the API host.</summary>
    protected virtual IServiceProvider Provider => _provider ??= BuildProvider();

    /// <summary>False when <see cref="Provider"/> comes from elsewhere (the API host in integration tests): no own registrations or mocks.</summary>
    protected virtual bool OwnsContainer => true;

    /// <summary>Called once per test before the container is built: fixture-wide registrations and settings.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    private ServiceProvider BuildProvider()
    {
        ConfigureServices(_services);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
        _services.AddSingleton<IConfiguration>(configuration);
        foreach (var (type, section) in OptionsTypes.Value)
            ConfigureOptions.MakeGenericMethod(type).Invoke(null, [_services, configuration.GetSection(section)]);

        _services.AddLogging(builder => builder.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        TryAdd<IClock>(Clock);
        TryAdd<IUnitOfWork>(UnitOfWork);

        var provider = _services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _provider = provider;
        return provider;
    }

    private void TryAdd<T>(T instance)
        where T : class
    {
        if (_services.All(d => d.ServiceType != typeof(T)))
            _services.AddSingleton(instance);
    }

    private Mock AddMock(Type type)
    {
        var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
        _mocks[type] = mock;
        _services.AddSingleton(type, _ => mock.Object);
        return mock;
    }

    private bool IsRegistered(Type type) =>
        _services.Any(d => d.ServiceType == type || (type.IsGenericType && d.ServiceType == type.GetGenericTypeDefinition()))
        || type == typeof(IClock) || type == typeof(IUnitOfWork) || type == typeof(IConfiguration)
        || (type.IsGenericType && type.GetGenericTypeDefinition() is var definition
                               && (definition == typeof(IOptions<>) || definition == typeof(ILogger<>)));

    private void RegisterWithDependencies(Type type)
    {
        if (IsRegistered(type))
            return;

        _services.AddSingleton(type);
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault()
                          ?? throw new InvalidOperationException($"{type.Name} has no public constructor.");
        foreach (var dependency in constructor.GetParameters().Select(p => p.ParameterType))
        {
            if (IsRegistered(dependency))
                continue;

            if (dependency.IsInterface || dependency.IsAbstract)
                AddMock(dependency);
            else if (dependency.Namespace?.StartsWith("PharmacyDelivery.", StringComparison.Ordinal) == true)
                RegisterWithDependencies(dependency);
        }
    }

    private static async ValueTask DisposeProviderAsync(IServiceProvider? provider)
    {
        if (provider is ServiceProvider owned)
            await owned.DisposeAsync();
    }
}
