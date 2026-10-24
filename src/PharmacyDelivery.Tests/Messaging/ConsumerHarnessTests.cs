using PharmacyDelivery.Infrastructure.Concrete;
using PharmacyDelivery.Messaging.Contracts.V1;
using PharmacyDelivery.Services.Abstract;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace PharmacyDelivery.Tests.Messaging;

[TestFixture]
public sealed class ConsumerHarnessTests : TestBase
{
    [Test]
    [Property("TestCase", "TC-7.15.3")]
    public async Task OrderPaidConsumer_HandsOrderToPharmacyService()
    {
        var service = new Mock<IPharmacyOrderService>();
        await using var provider = new ServiceCollection()
            .AddSingleton(service.Object)
            .AddMassTransitTestHarness(x => x.AddConsumer<OrderPaidConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var orderId = Guid.NewGuid();
        await harness.Bus.Publish(new OrderPaid(orderId, Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.That(await harness.GetConsumerHarness<OrderPaidConsumer>().Consumed.Any<OrderPaid>(), Is.True);
        service.Verify(s => s.AssignAfterPaymentAsync(orderId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
