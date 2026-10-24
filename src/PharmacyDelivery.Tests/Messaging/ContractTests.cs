using System.Text.Json;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Messaging.Contracts.V1;

namespace PharmacyDelivery.Tests.Messaging;

[TestFixture]
public sealed class ContractTests : TestBase
{
    [Test]
    [Property("TestCase", "TC-7.16.4")]
    public void Contracts_RoundTrip_AndTolerateUnknownFields()
    {
        var original = new OrderStatusChanged(Guid.NewGuid(), "PD-1", Guid.NewGuid(), Guid.NewGuid(), "Paid", "AssignedToPharmacy",
            DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(original, PharmacyDeliveryJsonOptions.Tolerant);
        var withNewField = json.TrimEnd('}') + ",\"addedInFutureVersion\":42}";

        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Deserialize<OrderStatusChanged>(json, PharmacyDeliveryJsonOptions.Tolerant), Is.EqualTo(original));
            Assert.That(JsonSerializer.Deserialize<OrderStatusChanged>(withNewField, PharmacyDeliveryJsonOptions.Tolerant),
                Is.EqualTo(original),
                "tolerant reader");
        });
    }

    [Test]
    [Property("TestCase", "TC-7.15.2")]
    public void Contracts_UsePrimitivesOnly_NoDomainTypes()
    {
        var contractTypes = typeof(OrderPaid).Assembly.GetTypes().Where(t => t.Namespace == typeof(OrderPaid).Namespace);
        var properties = contractTypes.SelectMany(t => t.GetProperties()).ToList();

        Assert.That(properties, Is.Not.Empty);
        Assert.That(properties.Where(p => p.PropertyType.Namespace?.StartsWith("PharmacyDelivery", StringComparison.Ordinal) == true),
            Is.Empty);
    }
}
