using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Domain.Payments;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Tests.Domain;

[TestFixture]
public sealed class ValueObjectAndEntityTests : TestBase
{
    [Test]
    public void Money_RoundsToCentsAndRejectsNegativeAndForeignCurrency()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Money.OfUsd(1.005m).Amount, Is.EqualTo(1.01m));
            Assert.That(() => Money.OfUsd(-1), Throws.InstanceOf<DomainException>());
            Assert.That(() => new Money(1, "EUR"), Throws.InstanceOf<DomainException>());
        });
    }

    [Test]
    public void GeoPoint_RejectsInvalidCoordinates()
    {
        Assert.That(() => new GeoPoint(91, 0), Throws.InstanceOf<DomainException>());
        Assert.That(() => new GeoPoint(0, -181), Throws.InstanceOf<DomainException>());
    }

    [Test]
    [Property("TestCase", "TC-7.5.2")]
    public void Payment_DuplicateNotificationIsIgnored_ConflictingOneThrows()
    {
        var payment = Payment.Start(OrderId.New(), "fake", "fake_1", Money.OfUsd(10), DomainBuilders.Now);

        Assert.Multiple(() =>
        {
            Assert.That(payment.MarkSucceeded(DomainBuilders.Now), Is.True);
            Assert.That(payment.MarkSucceeded(DomainBuilders.Now), Is.False, "duplicate is idempotent");
            Assert.That(() => payment.MarkFailed(DomainBuilders.Now), Throws.InstanceOf<DomainException>());
        });
    }

    [Test]
    [Property("TestCase", "TC-7.7.1")]
    public void Courier_CannotTakeTwoDeliveries_DeliveryFollowsItsLifecycle()
    {
        var courier = Courier.Create(CourierId.New(), UserId.New(), "Courier");
        courier.MarkBusy();
        var delivery = Delivery.Assign(DeliveryId.New(), OrderId.New(), courier.Id, PharmacyId.New(), DomainBuilders.Now);

        Assert.Multiple(() =>
        {
            Assert.That(() => courier.MarkBusy(), Throws.InstanceOf<DomainException>());
            Assert.That(() => delivery.Complete(DomainBuilders.Now), Throws.InstanceOf<DomainException>(), "cannot deliver before pickup");
        });

        delivery.PickUp(DomainBuilders.Now);
        delivery.Complete(DomainBuilders.Now);
        Assert.That(delivery.Status, Is.EqualTo(DeliveryStatus.Delivered));
    }

    [Test]
    [Property("TestCase", "TC-7.11.6")]
    public void User_LockoutAfterMaxAttempts_ExpiresAndCounterResets()
    {
        var user = User.Create(UserId.New(), "a@b.pl", "A B", "Customer", "hash", DomainBuilders.Now);
        for (var i = 0; i < 5; i++)
            user.RegisterFailedLogin(DomainBuilders.Now, 5, TimeSpan.FromMinutes(15));

        Assert.That(user.IsLockedOut(DomainBuilders.Now.AddMinutes(10)), Is.True);
        Assert.That(user.IsLockedOut(DomainBuilders.Now.AddMinutes(16)), Is.False);

        user.RegisterFailedLogin(DomainBuilders.Now.AddMinutes(16), 5, TimeSpan.FromMinutes(15));
        Assert.That(user.FailedLoginAttempts, Is.EqualTo(1), "a new series starts after the lockout expired");
    }
}
