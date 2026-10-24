using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Concrete;
using PharmacyDelivery.Infrastructure.Seed;

namespace PharmacyDelivery.Tests.Infrastructure;

[TestFixture]
public sealed class SeederRegistryTests : TestBase
{
    [SeedProfile("alpha", Order = 20)]
    [SeedProfile("beta", Order = 10)]
    private sealed class Shared : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [SeedProfile("alpha", Order = 10)]
    private sealed class First : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [SeedProfile("alpha", Order = 10)]
    private sealed class Duplicate : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Test]
    [Property("TestCase", "TC-7.19.1")]
    public void Registry_OrdersSeedersAndSupportsSeveralProfilesPerClass_CaseInsensitive()
    {
        var registry = SeederRegistry.FromTypes(typeof(Shared), typeof(First));

        Assert.Multiple(() =>
        {
            Assert.That(registry.GetSeeders("ALPHA"), Is.EqualTo(new[] { typeof(First), typeof(Shared) }));
            Assert.That(registry.GetSeeders("beta"), Is.EqualTo(new[] { typeof(Shared) }));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.19.2")]
    public void Registry_UnknownProfile_ListsAvailableOnes_DuplicateOrderIsRejected()
    {
        var registry = SeederRegistry.FromTypes(typeof(Shared));
        var unknown = Assert.Throws<InvalidOperationException>(() => registry.GetSeeders("gamma"));

        Assert.Multiple(() =>
        {
            Assert.That(unknown!.Message, Does.Contain("alpha").And.Contain("beta"));
            Assert.That(() => SeederRegistry.FromTypes(typeof(First), typeof(Duplicate)),
                Throws.InvalidOperationException.With.Message.Contains("same Order"));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.19.3")]
    public void ApplicationRegistry_ContainsDocumentedTemplates_AndIdsAreDeterministic()
    {
        var registry = new SeederRegistry([typeof(DataInitializer).Assembly]);

        Assert.Multiple(() =>
        {
            Assert.That(registry.Profiles, Is.SupersetOf(new[] { "demo", "empty", "large" }));
            Assert.That(DeterministicGuid.Create("pharmacy:NYC-MID"),
                Is.EqualTo(new Guid(DeterministicGuid.Create("pharmacy:NYC-MID").ToByteArray())));
            Assert.That(DeterministicGuid.Create("a"), Is.Not.EqualTo(DeterministicGuid.Create("b")));
        });
    }
}
