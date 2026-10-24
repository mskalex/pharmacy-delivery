using System.Globalization;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Payments;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Concrete;
using PharmacyDelivery.Tests.Domain;
using Moq;

namespace PharmacyDelivery.Tests.Services;

[TestFixture]
public sealed class PaymentServiceTests : TestBase
{
    private const string Secret = "unit-test-webhook-secret";
    private Order _order = null!;
    private Payment _payment = null!;
    private PaymentService _service = null!;

    private Mock<IPaymentGateway> Gateway => MockOf<IPaymentGateway>();

    [SetUp]
    public void SetUp()
    {
        Settings["Payments:WebhookSecret"] = Secret;
        _service = Service<PaymentService>();
        _order = DomainBuilders.NewOrder();
        _payment = Payment.Start(_order.Id, "fake", "fake_1", _order.Total, Now);
        MockOf<IOrderRepository>().Setup(o => o.GetForUpdateAsync(_order.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _order);
        var payments = MockOf<IPaymentRepository>();
        payments.Setup(p => p.FindByProviderIdAsync("fake", "fake_1", It.IsAny<CancellationToken>())).ReturnsAsync(() => _payment);
        payments.Setup(p => p.FindSucceededForOrderAsync(_order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _payment.Status == PaymentStatus.Succeeded ? _payment : null);
        Gateway.SetupGet(g => g.Provider).Returns("fake");
    }

    private Task<string> SendAsync(string status, string? signature = null)
    {
        var body =
            $$"""{"providerPaymentId":"fake_1","status":"{{status}}","amount":{{_order.Total.Amount.ToString(CultureInfo.InvariantCulture)}},"currency":"USD"}""";
        var ts = Now.ToUnixTimeSeconds();
        return _service.HandleWebhookAsync(body, ts, signature ?? HmacSignature.Compute(Secret, ts, body), Ct);
    }

    [Test]
    [Property("TestCase", "TC-7.5.3")]
    public void InvalidSignature_IsRejected()
    {
        var ex = Assert.ThrowsAsync<UnauthorizedException>(() => SendAsync("succeeded", signature: "deadbeef"));
        Assert.That(ex!.Code, Is.EqualTo(ErrorCodes.PaymentInvalidSignature));
    }

    [Test]
    [Property("TestCase", "TC-7.5.4")]
    public async Task DuplicateNotification_IsProcessedOnce()
    {
        Assert.That(await SendAsync("succeeded"), Is.EqualTo("paid"));
        Assert.That(await SendAsync("succeeded"), Is.EqualTo("duplicate"));
        Assert.That(_order.Status, Is.EqualTo(OrderStatus.Paid));
    }

    [Test]
    [Property("TestCase", "TC-7.5.5")]
    public async Task PaymentAfterReservationExpired_IsRefunded()
    {
        _order.Cancel("Reservation expired before payment.", Now);

        Assert.That(await SendAsync("succeeded"), Is.EqualTo("refund"));
        Gateway.Verify(g => g.RefundAsync("fake_1", _order.Total, It.IsAny<CancellationToken>()), Times.Once);
        Assert.That((_order.Status, _payment.Status), Is.EqualTo((OrderStatus.Refunded, PaymentStatus.Refunded)));
    }
}
