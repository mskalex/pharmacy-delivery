using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Tests.Domain;

[TestFixture]
public sealed class OrderStateMachineTests : TestBase
{
    private static readonly OrderStatus[] AllStatuses = Enum.GetValues<OrderStatus>();

    [Test]
    [Property("TestCase", "TC-7.9.1")]
    public void HappyPath_GoesThroughAllStatuses_AndRecordsHistoryAndEvents()
    {
        var order = DomainBuilders.NewOrder();
        var t = DomainBuilders.Now;

        order.MarkPaid(t.AddMinutes(1));
        order.AssignToPharmacy(t.AddMinutes(1));
        order.Accept(t.AddMinutes(2));
        order.MarkReadyForPickup(t.AddMinutes(5));
        order.MarkCourierAssigned(t.AddMinutes(6));
        order.MarkInDelivery(t.AddMinutes(8));
        order.MarkDelivered(t.AddMinutes(20));

        Assert.Multiple(() =>
        {
            Assert.That(order.Status, Is.EqualTo(OrderStatus.Delivered));
            Assert.That(order.Total, Is.EqualTo(Money.OfUsd(32.97m)));
            Assert.That(order.History.Select(h => h.ToStatus), Is.EqualTo(new[]
            {
                OrderStatus.AwaitingPayment, OrderStatus.Paid, OrderStatus.AssignedToPharmacy, OrderStatus.Accepted,
                OrderStatus.ReadyForPickup, OrderStatus.CourierAssigned, OrderStatus.InDelivery, OrderStatus.Delivered,
            }));
            Assert.That(order.DomainEvents.OfType<OrderStatusChangedDomainEvent>().Count(), Is.EqualTo(8));
        });
    }

    [Test]
    [Property("TestCase", "TC-7.9.2")]
    public void Transitions_MatchTheDocumentedTable([ValueSource(nameof(AllStatuses))] OrderStatus from)
    {
        // Every pair not listed in OrderStateMachine must be rejected; the listed ones allowed.
        foreach (var to in AllStatuses)
        {
            var allowed = OrderStateMachine.NextStatuses(from).Contains(to);
            Assert.That(OrderStateMachine.CanTransition(from, to), Is.EqualTo(allowed), $"{from} -> {to}");
        }
    }

    [Test]
    [Property("TestCase", "TC-7.9.3")]
    public void InvalidTransition_ThrowsDomainExceptionWithCode()
    {
        var order = DomainBuilders.NewOrder();

        var ex = Assert.Throws<DomainException>(() => order.Accept(DomainBuilders.Now));
        Assert.That(ex!.Code, Is.EqualTo(ErrorCodes.OrderInvalidTransition));
    }

    [Test]
    [Property("TestCase", "TC-7.6.1")]
    public void Reassign_MovesOrderToNextPharmacy_AndRemembersRejectedOne()
    {
        var first = PharmacyId.New();
        var second = PharmacyId.New();
        var order = DomainBuilders.NewOrder(first);
        order.MarkPaid(DomainBuilders.Now);
        order.AssignToPharmacy(DomainBuilders.Now);

        order.Reassign(second, DomainBuilders.Now.AddMinutes(3), DomainBuilders.Now.AddMinutes(40), "timeout");

        Assert.Multiple(() =>
        {
            Assert.That(order.PharmacyId, Is.EqualTo(second));
            Assert.That(order.Status, Is.EqualTo(OrderStatus.AssignedToPharmacy));
            Assert.That(order.RejectedPharmacyIds, Does.Contain(first.Value));
            Assert.That(() => order.Reassign(second, DomainBuilders.Now, DomainBuilders.Now, "x"), Throws.InstanceOf<DomainException>());
        });
    }
}
