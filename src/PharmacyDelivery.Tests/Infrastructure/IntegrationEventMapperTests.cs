using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Messaging.Contracts.V1;
using PharmacyDelivery.Services.Concrete;

namespace PharmacyDelivery.Tests.Infrastructure;

[TestFixture]
public sealed class IntegrationEventMapperTests : TestBase
{
    [TestCase(OrderStatus.Paid, typeof(OrderPaid))]
    [TestCase(OrderStatus.Accepted, typeof(OrderAcceptedByPharmacy))]
    [TestCase(OrderStatus.ReadyForPickup, typeof(OrderReadyForPickup))]
    [TestCase(OrderStatus.Delivered, typeof(DeliveryCompleted))]
    [Property("TestCase", "TC-7.15.1")]
    public void Map_AlwaysPublishesStatusChange_PlusSpecificEvent(OrderStatus status, Type expected)
    {
        var domainEvent = new OrderStatusChangedDomainEvent(OrderId.New(), "PD-1", UserId.New(), PharmacyId.New(), null, status,
            DateTimeOffset.UtcNow);

        var messages = IntegrationEventMapper.Map(domainEvent).ToList();

        Assert.That(messages.Select(m => m.GetType()), Is.EquivalentTo(new[] { typeof(OrderStatusChanged), expected }));
    }
}
