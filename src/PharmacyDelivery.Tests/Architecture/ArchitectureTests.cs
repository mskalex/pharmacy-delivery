using System.Reflection;
using PharmacyDelivery.Api.Controllers;
using PharmacyDelivery.Api.Infrastructure;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.CourierSimulator;
using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Infrastructure.Concrete;
using PharmacyDelivery.Messaging;
using PharmacyDelivery.Repository;
using PharmacyDelivery.Services.Abstract;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NetArchTest.Rules;

namespace PharmacyDelivery.Tests.Architecture;

/// <summary>Dependency rules between layers (DESIGN.md "Warstwy"). Broken rules fail the build in CI.</summary>
[TestFixture]
public sealed class ArchitectureTests : TestBase
{
    private static readonly Assembly Common = typeof(ErrorCodes).Assembly;
    private static readonly Assembly Domain = typeof(IAggregateRoot).Assembly;
    private static readonly Assembly Repository = typeof(PharmacyDeliveryDbContext).Assembly;
    private static readonly Assembly ServicesAssembly = typeof(IAuthService).Assembly;
    private static readonly Assembly Infrastructure = typeof(DataInitializer).Assembly;
    private static readonly Assembly Messaging = typeof(MessagingDefaults).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Simulator = typeof(CourierSimulatorWorker).Assembly;

    private static string Name(Assembly assembly) => assembly.GetName().Name!;

    private static IEnumerable<string> ReferencedPharmacyDeliveryAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("PharmacyDelivery.", StringComparison.Ordinal));

    [Test]
    [Property("TestCase", "TC-7.21.1")]
    public void Common_DependsOnNothing_AndHasNoPackages()
    {
        Assert.That(ReferencedPharmacyDeliveryAssemblies(Common), Is.Empty);
        var thirdParty = Common.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard" &&
                        !n.StartsWith("Microsoft.Win32", StringComparison.Ordinal));
        Assert.That(thirdParty, Is.Empty, "PharmacyDelivery.Common may use the BCL only");
    }

    [Test]
    [Property("TestCase", "TC-7.21.2")]
    public void Layers_ReferenceOnlyTheLayersBelow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReferencedPharmacyDeliveryAssemblies(Domain), Is.EquivalentTo(new[] { Name(Common) }));
            Assert.That(ReferencedPharmacyDeliveryAssemblies(Messaging), Is.Empty, "contracts are self-contained");
            Assert.That(ReferencedPharmacyDeliveryAssemblies(Repository), Is.SubsetOf(new[] { Name(Common), Name(Domain) }));
            Assert.That(ReferencedPharmacyDeliveryAssemblies(ServicesAssembly),
                Is.SubsetOf(new[] { Name(Common), Name(Domain), Name(Repository), Name(Messaging) }));
            Assert.That(ReferencedPharmacyDeliveryAssemblies(Simulator), Is.EquivalentTo(new[] { Name(Common) }));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.21.3")]
    public void Domain_IsPersistenceAndFrameworkIgnorant()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "MassTransit", "MediatR", "StackExchange.Redis",
                "Microsoft.AspNetCore")
            .GetResult();

        Assert.That(result.IsSuccessful, Is.True, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Test]
    [Property("TestCase", "TC-7.21.4")]
    public void Services_UseRepositoryAbstractionsOnly_NoEntityFramework()
    {
        var result = Types.InAssembly(ServicesAssembly)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "StackExchange.Redis",
                "PharmacyDelivery.Repository.PharmacyDeliveryDbContext")
            .GetResult();

        Assert.That(result.IsSuccessful, Is.True, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Test]
    [Property("TestCase", "TC-7.21.5")]
    public void Controllers_AreThin_NoDatabaseOrRepositoryAccess()
    {
        var result = Types.InAssembly(Api)
            .That().ResideInNamespace("PharmacyDelivery.Api.Controllers")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "PharmacyDelivery.Repository.PharmacyDeliveryDbContext",
                "PharmacyDelivery.Repository.Abstractions", "StackExchange.Redis")
            .GetResult();

        Assert.That(result.IsSuccessful, Is.True, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Test]
    [Property("TestCase", "TC-7.21.6")]
    public void DependencyInjection_IsConfiguredInOneFilePerHost()
    {
        var allowed = new[]
            { "PharmacyDelivery.Api.DependencyInjection", "Program", "PharmacyDelivery.CourierSimulator.DependencyInjection" };
        var offenders = new[] { Common, Domain, Repository, ServicesAssembly, Infrastructure, Messaging, Api, Simulator }
            .SelectMany(a =>
                Types.InAssembly(a).That().HaveDependencyOn("Microsoft.Extensions.DependencyInjection.IServiceCollection").GetTypes())
            .Select(t => t.FullName!)
            .Where(n => !allowed.Any(a =>
                n == a || n.StartsWith(a + "+", StringComparison.Ordinal) || n.StartsWith("Program+", StringComparison.Ordinal)))
            .ToList();

        Assert.That(offenders, Is.Empty, "IServiceCollection may be used only in DependencyInjection.cs");
    }

    [Test]
    [Property("TestCase", "TC-7.24.1")]
    public void EveryStateChangingAction_IsAuditedOrExplicitlyExempt()
    {
        var actions = Api.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>()
                .Any(h => h.HttpMethods.Any(v => v is "POST" or "PUT" or "DELETE" or "PATCH")))
            .ToList();

        var missing = actions
            .Where(m => m.GetCustomAttribute<AuditedAttribute>() is null && m.GetCustomAttribute<AuditExemptAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.That(actions, Is.Not.Empty);
        Assert.That(missing, Is.Empty);
    }

    [Test]
    [Property("TestCase", "TC-7.12.2")]
    public void EveryController_RequiresAuthenticationByDefault_AnonymousEndpointsAreExplicit()
    {
        // The fallback policy requires an authenticated user; only these controllers/actions opt out.
        var anonymous = Api.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                            || t.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                .Select(m => $"{t.Name}.{m.Name}"))
            .Order()
            .ToList();

        Assert.That(anonymous, Is.EquivalentTo(new[]
        {
            "AuthController.Login", "AuthController.Refresh", "AuthController.Register",
            "CatalogController.Get", "CatalogController.Search",
            "DevPspController.Complete", "DevPspController.Page",
            "JwksController.Jwks",
            "PaymentWebhooksController.Receive",
            "PharmaciesController.List",
        }));
        Assert.That(typeof(ApiControllerBase).GetCustomAttribute<ApiControllerAttribute>(), Is.Not.Null);
    }
}
