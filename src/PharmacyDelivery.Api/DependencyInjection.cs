using System.Data;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using FluentValidation;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PharmacyDelivery.Api.Configuration;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Api.Controllers;
using PharmacyDelivery.Api.Hubs;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Concrete;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Infrastructure.Concrete;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Messaging;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Concrete;
using PharmacyDelivery.Repository.Options;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Concrete;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Options;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PharmacyDelivery.Api;

/// <summary>
/// The single place where the application's dependency injection is configured (project rule: no DI code in any
/// other project; an architecture test checks that IServiceCollection is used only here and in the simulator's
/// own file). Sections run in a fixed order; lifetimes are documented next to each registration group.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPharmacyDelivery(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILoggingBuilder logging)
    {
        AppInfo.Environment = environment.EnvironmentName;

        services
            .AddPharmacyDeliveryOptions(configuration)
            .AddPharmacyDeliveryServices()
            .AddPharmacyDeliveryPersistence(configuration)
            .AddPharmacyDeliveryRedis(configuration)
            .AddPharmacyDeliveryMessaging()
            .AddPharmacyDeliveryBackgroundJobs()
            .AddPharmacyDeliveryExternalServices(configuration)
            .AddPharmacyDeliverySeeding()
            .AddPharmacyDeliveryAuth(configuration)
            .AddPharmacyDeliveryWeb(configuration)
            .AddPharmacyDeliveryObservability(configuration, logging);

        return services;
    }

    // ---- Options: every section validated on startup (DataAnnotations + environment guards) ------------------

    private static IServiceCollection AddPharmacyDeliveryOptions(this IServiceCollection services, IConfiguration configuration)
    {
        Bind<CheckoutOptions>(services, configuration, CheckoutOptions.SectionName);
        Bind<PharmacyOptions>(services, configuration, PharmacyOptions.SectionName);
        Bind<DispatchOptions>(services, configuration, DispatchOptions.SectionName);
        Bind<TrackingOptions>(services, configuration, TrackingOptions.SectionName);
        Bind<EtaOptions>(services, configuration, EtaOptions.SectionName);
        Bind<PaymentsOptions>(services, configuration, PaymentsOptions.SectionName);
        Bind<AuthOptions>(services, configuration, AuthOptions.SectionName);
        Bind<DatabaseOptions>(services, configuration, DatabaseOptions.SectionName);
        Bind<AuditOptions>(services, configuration, AuditOptions.SectionName);
        Bind<RedisOptions>(services, configuration, RedisOptions.SectionName);
        Bind<RabbitMqOptions>(services, configuration, RabbitMqOptions.SectionName);
        Bind<SeedOptions>(services, configuration, SeedOptions.SectionName);
        Bind<JobsOptions>(services, configuration, JobsOptions.SectionName);
        Bind<OpenTelemetryOptions>(services, configuration, OpenTelemetryOptions.SectionName);
        Bind<SwaggerOptions>(services, configuration, SwaggerOptions.SectionName);
        Bind<CorsOptions>(services, configuration, CorsOptions.SectionName);
        Bind<SecurityOptions>(services, configuration, SecurityOptions.SectionName);
        Bind<RateLimitingOptions>(services, configuration, RateLimitingOptions.SectionName);
        Bind<ErrorHandlingOptions>(services, configuration, ErrorHandlingOptions.SectionName);
        Bind<AdminOptions>(services, configuration, AdminOptions.SectionName);
        Bind<StartupReportOptions>(services, configuration, StartupReportOptions.SectionName);

        services.AddSingleton<EnvironmentGuards>();
        services.AddSingleton<IValidateOptions<PaymentsOptions>>(sp => sp.GetRequiredService<EnvironmentGuards>());
        services.AddSingleton<IValidateOptions<AuthOptions>>(sp => sp.GetRequiredService<EnvironmentGuards>());
        services.AddSingleton<IValidateOptions<SeedOptions>>(sp => sp.GetRequiredService<EnvironmentGuards>());
        return services;
    }

    // ---- Services layer (scoped: one per request / consumer / job tick) --------------------------------------

    private static IServiceCollection AddPharmacyDeliveryServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<PasswordPolicyValidator>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<TrackingPushThrottle>();

        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<IPharmacySelector, PharmacySelector>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IStockReservationService, StockReservationService>();
        services.AddScoped<IStockSyncService, StockSyncService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IPharmacyOrderService, PharmacyOrderService>();
        services.AddScoped<IPharmacyDirectoryService, PharmacyDirectoryService>();
        services.AddScoped<IDispatchService, DispatchService>();
        services.AddScoped<ICourierDeliveryService, CourierDeliveryService>();
        services.AddScoped<ITrackingService, TrackingService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditService, AuditService>();

        // Domain events: MediatR 12.x (Apache-2.0) notifications dispatched by the unit of work.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<MediatRDomainEventDispatcher>();
            cfg.NotificationPublisherType = typeof(TracingNotificationPublisher);
            cfg.Lifetime = ServiceLifetime.Scoped;
        });
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        return services;
    }

    // ---- Repository: DbContext (scoped), repositories, queries, interceptors ---------------------------------

    private static IServiceCollection AddPharmacyDeliveryPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = Required(configuration, "ConnectionStrings:" + PharmacyDeliveryDbContextConfiguration.ConnectionStringName);

        services.AddScoped<IAuditContext, AuditContext>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddSingleton<DbCommandDiagnosticsInterceptor>();
        services.AddSingleton<PostgresErrorTranslator>();
        services.AddDbContext<PharmacyDeliveryDbContext>((sp, options) =>
        {
            PharmacyDeliveryDbContextConfiguration.Configure(options, connectionString,
                sp.GetRequiredService<IOptions<DatabaseOptions>>().Value);
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                sp.GetRequiredService<DbCommandDiagnosticsInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<DatabaseMaintenance>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IStockReservationRepository, StockReservationRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IPharmacyRepository, PharmacyRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ICourierRepository, CourierRepository>();
        services.AddScoped<IDeliveryRepository, DeliveryRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthTokenRepository, AuthTokenRepository>();

        services.AddScoped<ICatalogQueries, CatalogQueries>();
        services.AddScoped<IPharmacyQueries, PharmacyQueries>();
        services.AddScoped<IOrderQueries, OrderQueries>();
        services.AddScoped<IDeliveryQueries, DeliveryQueries>();
        services.AddScoped<IUserQueries, UserQueries>();
        services.AddScoped<IAuditQueries, AuditQueries>();
        return services;
    }

    // ---- Redis: one multiplexer (singleton), HybridCache L2, stores, locks -----------------------------------

    private static IServiceCollection AddPharmacyDeliveryRedis(this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnection = Required(configuration, "ConnectionStrings:Redis");
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnection);
            options.AbortOnConnectFail = false; // start even if Redis is still booting; health check reports it
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddSingleton<RedisKeys>();
        services.AddSingleton<ICartStore, RedisCartStore>();
        services.AddSingleton<ICourierLocationStore, RedisCourierLocationStore>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddSingleton<IRevokedTokenCache, RedisRevokedTokenCache>();

        // HybridCache: L1 in-process memory + L2 Redis (IDistributedCache) for the catalog.
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>().Configure<IConnectionMultiplexer, IOptions<RedisOptions>>((options, mux, redis) =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult(mux);
            options.InstanceName = redis.Value.KeyPrefix + "cache:";
        });
        services.AddHybridCache(options =>
        {
            options.MaximumPayloadBytes = 1024 * 1024;
            options.DefaultEntryOptions = new() { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(1) };
        });
        return services;
    }

    // ---- Messaging: MassTransit 8.x (Apache-2.0) over RabbitMQ with the EF Core transactional outbox/inbox ----

    private static IServiceCollection AddPharmacyDeliveryMessaging(this IServiceCollection services)
    {
        Type[] nonRetriable =
        [
            typeof(DomainException), typeof(BusinessRuleException), typeof(NotFoundException),
            typeof(ForbiddenException), typeof(ValidationFailedException), typeof(UnauthorizedException),
        ];

        services.AddScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>();
        services.AddMassTransit(bus =>
        {
            bus.SetEndpointNameFormatter(MessagingDefaults.EndpointNameFormatter);
            bus.AddConsumer<OrderPaidConsumer>();
            bus.AddConsumer<OrderReadyForPickupConsumer>();
            bus.AddConsumer<OrderStatusChangedConsumer>();

            bus.AddEntityFrameworkOutbox<PharmacyDeliveryDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
                outbox.IsolationLevel = IsolationLevel.ReadCommitted;
                outbox.QueryDelay = TimeSpan.FromMilliseconds(500);
                outbox.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
            {
                // Retry wraps the outbox: every attempt gets a fresh transaction; then the message goes to *_error.
                endpoint.UseMessageRetry(retry => MessagingDefaults.ConfigureRetry(retry, nonRetriable));
                endpoint.UseEntityFrameworkOutbox<PharmacyDeliveryDbContext>(context);
            });

            bus.UsingRabbitMq((context, cfg) =>
            {
                var rabbit = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                cfg.Host(rabbit.Host, rabbit.Port, rabbit.VirtualHost, host =>
                {
                    host.Username(rabbit.Username);
                    host.Password(rabbit.Password);
                });
                cfg.ConcurrentMessageLimit = MessagingDefaults.ConcurrentMessageLimit;
                cfg.PrefetchCount = MessagingDefaults.PrefetchCount;
                cfg.ConfigureJsonSerializerOptions(options =>
                {
                    PharmacyDeliveryJsonOptions.Apply(options, strict: false);
                    return options;
                });
                cfg.ConfigureEndpoints(context);
            });
        });
        return services;
    }

    // ---- Background jobs (hosted services, singletons creating a scope per tick) -----------------------------

    private static IServiceCollection AddPharmacyDeliveryBackgroundJobs(this IServiceCollection services)
    {
        services.AddSingleton<LocationHistoryChannel>();
        services.AddSingleton<ILocationHistoryQueue>(sp => sp.GetRequiredService<LocationHistoryChannel>());
        services.AddHostedService<LocationHistoryWriter>();
        services.AddHostedService<ReservationExpiryJob>();
        services.AddHostedService<AcceptanceTimeoutJob>();
        services.AddHostedService<DispatchRetryJob>();
        services.AddHostedService<StockSyncJob>();
        services.AddHostedService<RetentionCleanupJob>();
        services.AddHostedService<CourierLocationPartitionJob>();
        return services;
    }

    // ---- Adapters of external systems ------------------------------------------------------------------------

    private static IServiceCollection AddPharmacyDeliveryExternalServices(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Payments:Provider"];
        if (provider != "Fake")
        {
            throw new InvalidOperationException(
                $"Payments:Provider='{provider}': only the Fake PSP adapter is part of this sample; a real PSP adapter (Stripe) is out of scope (DESIGN.md §2).");
        }

        services.AddSingleton<IEtaService, RoutingEtaService>();
        services.AddSingleton<IPharmacyStockFeed, FakePharmacyStockFeed>();
        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.AddHttpClient<FakePspSimulator>((sp, client) =>
            client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<PaymentsOptions>>().Value.FakeWebhookBaseUrl));
        services.AddHttpClient(DataInitializer.RabbitManagementClient, (sp, client) =>
        {
            var rabbit = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            client.BaseAddress = new Uri(rabbit.ManagementUrl);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(rabbit.Username + ":" + rabbit.Password));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });
        return services;
    }

    // ---- Seed: templates chosen by string, discovered via [SeedProfile] -------------------------------------

    private static IServiceCollection AddPharmacyDeliverySeeding(this IServiceCollection services)
    {
        services.AddSingleton(new SeederRegistry([typeof(DataInitializer).Assembly]));
        services.AddSingleton<SeedDataSource>();
        services.AddScoped<SeedPasswordHasher>();
        services.AddScoped<DataInitializer>();
        services.AddScoped<IDataMaintenance>(sp => sp.GetRequiredService<DataInitializer>());
        return services;
    }

    // ---- Authentication (JWT: local ES256 issuer or external OIDC) and authorization -------------------------

    private static IServiceCollection AddPharmacyDeliveryAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration["Auth:Mode"] ?? "Local";
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddSingleton<JwtSigningKeyProvider>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IServiceProvider, IOptions<AuthOptions>>((options, sp, auth) =>
            {
                var settings = auth.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = ClaimNames.Subject;
                options.TokenValidationParameters.RoleClaimType = mode == "Oidc" ? settings.Oidc.RoleClaim : ClaimNames.Role;
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
                if (mode == "Oidc")
                {
                    options.Authority = settings.Oidc.Authority;
                    options.Audience = settings.Oidc.Audience ?? settings.Audience;
                    options.RequireHttpsMetadata = settings.Oidc.RequireHttpsMetadata;
                }
                else
                {
                    var keys = sp.GetRequiredService<JwtSigningKeyProvider>();
                    options.TokenValidationParameters.ValidIssuer = settings.Issuer;
                    options.TokenValidationParameters.ValidAudience = settings.Audience;
                    options.TokenValidationParameters.IssuerSigningKey = keys.ValidationKey;
                    options.TokenValidationParameters.ValidAlgorithms = ["ES256"];
                }

                options.Events = new JwtBearerEvents
                {
                    // Browsers cannot send headers on WebSockets: the hub accepts the token from the query string.
                    OnMessageReceived = context =>
                    {
                        if (context.HttpContext.Request.Path.StartsWithSegments("/hubs")
                            && context.Request.Query.TryGetValue("access_token", out var token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },

                    // Access tokens are registered by jti: logout/block revokes them before they expire.
                    OnTokenValidated = async context =>
                    {
                        if (mode != "Local")
                            return;

                        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimNames.TokenId), out var jti)
                            || await context.HttpContext.RequestServices.GetRequiredService<IAuthService>()
                                .IsAccessTokenRevokedAsync(jti, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The access token has been revoked.");
                        }
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Customer, p => p.RequireRole(Roles.Customer))
            .AddPolicy(Policies.Pharmacist, p => p.RequireRole(Roles.Pharmacist).RequireClaim(ClaimNames.PharmacyId))
            .AddPolicy(Policies.Courier, p => p.RequireRole(Roles.Courier).RequireClaim(ClaimNames.CourierId))
            .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));
        return services;
    }

    // ---- Web: MVC, JSON, validation, ProblemDetails, versioning, Swagger, SignalR, rate limiting, CORS, health

    private static IServiceCollection AddPharmacyDeliveryWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options => options.Filters.Add<ValidationFilter>())
            .AddJsonOptions(options => PharmacyDeliveryJsonOptions.Apply(options.JsonSerializerOptions, strict: true))
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = ProblemDetailsCustomization.InvalidModelState);
        services.AddValidatorsFromAssemblyContaining<CheckoutRequestValidator>(ServiceLifetime.Singleton);
        services.AddScoped<ValidationFilter>();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsCustomization.Customize);
        services.AddExceptionHandler<PharmacyDeliveryExceptionHandler>();

        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = false;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

        services.AddSignalR(options => options.MaximumReceiveMessageSize = 32 * 1024)
            .AddJsonProtocol(options => PharmacyDeliveryJsonOptions.Apply(options.PayloadSerializerOptions, strict: false))
            .AddStackExchangeRedis(Required(configuration, "ConnectionStrings:Redis"), options =>
                options.Configuration.ChannelPrefix = RedisChannel.Literal("pharmacy-delivery:signalr"));
        services.AddSingleton<IOrderNotifier, SignalROrderNotifier>();

        services.AddRateLimiter(options =>
            ConfigureRateLimiter(options, configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() ?? new()));

        var origins = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>()?.AllowedOrigins ?? [];
        services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.WithOrigins(origins).AllowAnyHeader().WithMethods("GET", "POST", "PUT", "DELETE").AllowCredentials()));

        var trustAllProxies = configuration.GetValue<bool>("Security:TrustAllForwardedHeaders");
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            if (trustAllProxies)
            {
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            }
        });
        services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
        services.Configure<KestrelServerOptions>(options =>
            options.Limits.MaxRequestBodySize = 256 * 1024);

        services.AddHealthChecks()
            .AddNpgSql(Required(configuration, "ConnectionStrings:" + PharmacyDeliveryDbContextConfiguration.ConnectionStringName),
                name: "postgres",
                tags: ["ready"])
            .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: "redis", tags: ["ready"]);
        return services;
    }

    // ---- Observability: OpenTelemetry traces, metrics and logs (NLog stays the primary log sink) --------------

    private static IServiceCollection AddPharmacyDeliveryObservability(this IServiceCollection services, IConfiguration configuration,
        ILoggingBuilder logging)
    {
        var otel = configuration.GetSection(OpenTelemetryOptions.SectionName).Get<OpenTelemetryOptions>() ?? new();
        if (!otel.Enabled)
            return services;

        var exportEnabled = !string.IsNullOrWhiteSpace(otel.OtlpEndpoint);

        void Otlp(OtlpExporterOptions o)
        {
            o.Endpoint = new Uri(otel.OtlpEndpoint!);
            o.Protocol = otel.OtlpProtocol == "grpc" ? OtlpExportProtocol.Grpc : OtlpExportProtocol.HttpProtobuf;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(otel.ServiceName, serviceVersion: AppInfo.Version)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment.name", AppInfo.Environment)]))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(otel.SamplingRatio)))
                    .AddAspNetCoreInstrumentation(o => o.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health") && !context.Request.Path.StartsWithSegments("/swagger"))
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddRedisInstrumentation()
                    .AddSource("MassTransit")
                    .AddSource("Microsoft.AspNetCore.SignalR.Server")
                    .AddSource(PharmacyDeliveryTelemetry.SourceName);
                if (exportEnabled)
                    tracing.AddOtlpExporter(Otlp);
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter("System.Runtime", "Microsoft.AspNetCore.Server.Kestrel", "MassTransit", "Npgsql",
                        PharmacyDeliveryTelemetry.SourceName);
                if (exportEnabled)
                    metrics.AddOtlpExporter(Otlp);
            });

        if (exportEnabled && otel.ExportLogs)
        {
            logging.AddOpenTelemetry(options =>
            {
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
                options.AddOtlpExporter(Otlp);
            });
        }

        return services;
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------

    private static void Bind<T>(IServiceCollection services, IConfiguration configuration, string section)
        where T : class =>
        services.AddOptions<T>().Bind(configuration.GetSection(section)).ValidateDataAnnotations().ValidateOnStart();

    private static string Required(IConfiguration configuration, string key) =>
        configuration[key] is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Configuration value '{key}' is required (see docs/configuration.md).");

    private static void ConfigureRateLimiter(RateLimiterOptions options, RateLimitingOptions limits)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.Headers.RetryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                ? ((int)retry.TotalSeconds).ToString(CultureInfo.InvariantCulture)
                : "60";
            var problem = new ProblemDetails
                { Status = StatusCodes.Status429TooManyRequests, Title = "Too Many Requests", Detail = "Rate limit exceeded." };
            problem.Extensions["code"] = ErrorCodes.RateLimited;
            await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                .WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problem });
        };

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            http.Request.Path.StartsWithSegments("/health")
                ? RateLimitPartition.GetNoLimiter("health")
                : RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => Window(limits.Global)));
        options.AddPolicy(RateLimits.Auth,
            http => RateLimitPartition.GetFixedWindowLimiter("auth:" + ClientIp(http), _ => Window(limits.Auth)));
        options.AddPolicy(RateLimits.Checkout,
            http => RateLimitPartition.GetFixedWindowLimiter("checkout:" + UserOrIp(http), _ => Window(limits.Checkout)));
        options.AddPolicy(RateLimits.Tracking,
            http => RateLimitPartition.GetFixedWindowLimiter("tracking:" + UserOrIp(http), _ => Window(limits.Tracking)));

        static FixedWindowRateLimiterOptions Window(RateLimitRule rule) =>
            new() { PermitLimit = rule.PermitLimit, Window = TimeSpan.FromSeconds(rule.WindowSeconds), QueueLimit = 0 };

        static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        static string UserOrIp(HttpContext http) => http.User.FindFirstValue(ClaimNames.Subject) ?? ClientIp(http);
    }
}
