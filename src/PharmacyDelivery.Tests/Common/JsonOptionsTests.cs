using System.Text.Json;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Domain.Ordering;

namespace PharmacyDelivery.Tests.Common;

[TestFixture]
public sealed class JsonOptionsTests : TestBase
{
    private sealed record Sample(string ProductName, OrderStatus Status, decimal Price, string? Note);

    [Test]
    [Property("TestCase", "TC-7.16.1")]
    public void Serialize_UsesCamelCaseEnumStringsDecimalAndSkipsNulls()
    {
        var json = JsonSerializer.Serialize(new Sample("Paracetamol", OrderStatus.InDelivery, 6.49m, null),
            PharmacyDeliveryJsonOptions.Strict);

        Assert.That(json, Is.EqualTo("""{"productName":"Paracetamol","status":"inDelivery","price":6.49}"""));
    }

    [Test]
    [Property("TestCase", "TC-7.16.2")]
    public void Strict_RejectsUnknownMembers_TolerantSkipsThem()
    {
        const string json = """{"productName":"X","status":"paid","price":1,"note":null,"unexpected":true}""";

        Assert.That(() => JsonSerializer.Deserialize<Sample>(json, PharmacyDeliveryJsonOptions.Strict), Throws.InstanceOf<JsonException>());
        Assert.That(JsonSerializer.Deserialize<Sample>(json, PharmacyDeliveryJsonOptions.Tolerant)!.ProductName, Is.EqualTo("X"));
    }

    [Test]
    [Property("TestCase", "TC-7.16.3")]
    public void Deserialize_RejectsIntegerEnumsAndExcessiveDepth()
    {
        Assert.That(
            () => JsonSerializer.Deserialize<Sample>("""{"productName":"X","status":3,"price":1}""", PharmacyDeliveryJsonOptions.Strict),
            Throws.InstanceOf<JsonException>());

        var deep = string.Concat(Enumerable.Repeat("[", PharmacyDeliveryJsonOptions.MaxDepth + 1)) +
                   string.Concat(Enumerable.Repeat("]", PharmacyDeliveryJsonOptions.MaxDepth + 1));
        Assert.That(() => JsonSerializer.Deserialize<object>(deep, PharmacyDeliveryJsonOptions.Strict), Throws.InstanceOf<JsonException>());
    }
}
