using PharmacyDelivery.Tests.Integration;
using NUnit.Framework.Interfaces;

namespace PharmacyDelivery.Tests.Seed;

/// <summary>
/// Resets the database and loads the named seed template before the test (same code path as <c>Seed:Mode=Reset</c>).
/// Put it on the fixture; a method-level attribute overrides the fixture's template. Names: <see cref="TestSeedProfiles"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class UseSeedAttribute(string profile) : Attribute, ITestAction
{
    public string Profile { get; } = profile;

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        // NUnit creates new attribute instances per query, so the class-level attribute is recognised by its profile.
        var methodLevel = test.Method?.GetCustomAttributes<UseSeedAttribute>(false) ?? [];
        if (methodLevel.Length > 0 && methodLevel.All(a => a.Profile != Profile))
            return; // the method declares its own template

        IntegrationEnvironment.ResetAsync(Profile).GetAwaiter().GetResult();
    }

    public void AfterTest(ITest test)
    {
    }
}
