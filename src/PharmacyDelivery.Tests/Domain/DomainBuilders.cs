using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Ordering;
using PharmacyDelivery.Domain.Shared;

namespace PharmacyDelivery.Tests.Domain;

internal static class DomainBuilders
{
    public static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);

    public static Order NewOrder(PharmacyId? pharmacy = null) =>
        Order.Create(
            OrderId.New(),
            "PD-TEST-1",
            UserId.New(),
            pharmacy ?? PharmacyId.New(),
            new DeliveryAddress("11 E 36th St", "New York", "10016", 40.7493, -73.9822),
            [
                new OrderLine(ProductId.New(), "Acetaminophen", Money.OfUsd(6.49m), 2),
                new OrderLine(ProductId.New(), "Vitamin C", Money.OfUsd(19.99m), 1)
            ],
            "key-12345678",
            Now,
            Now.AddMinutes(30));
}
