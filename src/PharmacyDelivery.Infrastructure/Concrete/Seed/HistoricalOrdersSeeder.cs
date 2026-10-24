using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Deliveries;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Payments;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// History so the demo is not empty: a delivered and a refunded order of Emily, and David's order currently in
/// delivery with courier 6 (the courier simulator drives it to the door right after startup).
/// </summary>
[SeedProfile("demo", Order = 60)]
public sealed class HistoricalOrdersSeeder(PharmacyDeliveryDbContext db, SeedDataSource data, IClock clock) : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var users = data.Read<UsersJson>("users.json");
        var emily = users.Customers[0];
        var david = users.Customers[1];
        var now = clock.UtcNow;
        var products = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken);

        OrderLine Line(int index, int quantity)
        {
            var product = products[SeedKeys.Product(index)];
            return new OrderLine(product.Id, product.Name, product.Price, quantity);
        }

        // 1. Delivered two days ago.
        var delivered = NewOrder("seed-1", emily, "NYC-MID", [Line(0, 2), Line(20, 1)], now.AddDays(-2));
        var t = now.AddDays(-2);
        delivered.MarkPaid(t.AddMinutes(1));
        delivered.AssignToPharmacy(t.AddMinutes(1));
        delivered.Accept(t.AddMinutes(3));
        delivered.MarkReadyForPickup(t.AddMinutes(10));
        delivered.MarkCourierAssigned(t.AddMinutes(11));
        delivered.MarkInDelivery(t.AddMinutes(15));
        delivered.MarkDelivered(t.AddMinutes(32));
        var courier1 = SeedKeys.Courier(users.Couriers[0].Email);
        var delivery1 = Delivery.Assign(DeliveryId.New(), delivered.Id, courier1, delivered.PharmacyId, t.AddMinutes(11));
        delivery1.PickUp(t.AddMinutes(15));
        delivery1.Complete(t.AddMinutes(32));
        AddPayment(delivered, t.AddMinutes(1), refunded: false);
        db.Deliveries.Add(delivery1);

        // 2. Paid, then cancelled by the customer and refunded.
        var cancelled = NewOrder("seed-2", emily, "NYC-SOH", [Line(32, 1)], now.AddDays(-1));
        var c = now.AddDays(-1);
        cancelled.MarkPaid(c.AddMinutes(1));
        cancelled.AssignToPharmacy(c.AddMinutes(1));
        cancelled.Cancel("The customer changed their mind.", c.AddMinutes(2));
        cancelled.MarkRefunded(c.AddMinutes(3));
        AddPayment(cancelled, c.AddMinutes(1), refunded: true);

        // 3. In delivery right now (courier 6 is on the way from BKN-WBG).
        var inDelivery = NewOrder("seed-3", david, "BKN-WBG", [Line(12, 1), Line(14, 1)], now.AddMinutes(-20));
        var d = now.AddMinutes(-20);
        inDelivery.MarkPaid(d.AddMinutes(1));
        inDelivery.AssignToPharmacy(d.AddMinutes(1));
        inDelivery.Accept(d.AddMinutes(2));
        inDelivery.MarkReadyForPickup(d.AddMinutes(8));
        inDelivery.MarkCourierAssigned(d.AddMinutes(9));
        inDelivery.MarkInDelivery(d.AddMinutes(15));
        AddPayment(inDelivery, d.AddMinutes(1), refunded: false);
        var courier6 = SeedKeys.Courier(users.Couriers[5].Email);
        var delivery3 = Delivery.Assign(DeliveryId.New(), inDelivery.Id, courier6, inDelivery.PharmacyId, d.AddMinutes(9));
        delivery3.PickUp(d.AddMinutes(15));
        db.Deliveries.Add(delivery3);
        (await db.Couriers.FirstAsync(x => x.Id == courier6, cancellationToken)).MarkBusy();

        foreach (var order in new[] { delivered, cancelled, inDelivery })
        {
            order.ClearDomainEvents();
            db.Orders.Add(order);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Order NewOrder(string key, PersonJson customer, string pharmacyCode, IReadOnlyCollection<OrderLine> lines,
        DateTimeOffset createdAt)
    {
        var address = customer.Addresses![0];
        return Order.Create(
            new OrderId(DeterministicGuid.Create("order:" + key)),
            "PD-SEED-" + key[^1],
            SeedKeys.User(customer.Email),
            SeedKeys.Pharmacy(pharmacyCode),
            new DeliveryAddress(address.Street, address.City, address.PostalCode, address.Latitude, address.Longitude),
            lines,
            key,
            createdAt,
            createdAt.AddMinutes(35));
    }

    private void AddPayment(Order order, DateTimeOffset paidAt, bool refunded)
    {
        var payment = Payment.Start(order.Id, "fake", "fake_seed_" + order.Number, order.Total, paidAt.AddSeconds(-30));
        payment.MarkSucceeded(paidAt);
        if (refunded)
            payment.MarkRefunded(paidAt.AddMinutes(2));

        db.Payments.Add(payment);
    }
}
