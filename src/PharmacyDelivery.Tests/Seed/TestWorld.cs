using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Tests.Seed;

/// <summary>Deterministic test data shared by the test seed templates (ids are known to the tests).</summary>
public static class TestWorld
{
    public const string Password = "Fixture-Password-2026!";
    public const string Customer = "customer@test.local";
    public const string OtherCustomer = "other.customer@test.local";
    public const string Admin = "admin@test.local";
    public const string Pharmacist1 = "pharmacist.t1@test.local";
    public const string Pharmacist2 = "pharmacist.t2@test.local";
    public const string Courier = "courier.t1@test.local";
    public const string NearCourier = "courier.near@test.local";
    public const string FarCourier = "courier.far@test.local";
    public const int ScarceStock = 10;

    public static readonly PharmacyId Pharmacy1 = new(DeterministicGuid.Create("test:pharmacy:1"));
    public static readonly PharmacyId Pharmacy2 = new(DeterministicGuid.Create("test:pharmacy:2"));
    public static readonly ProductId Product1 = new(DeterministicGuid.Create("test:product:1"));
    public static readonly ProductId Product2 = new(DeterministicGuid.Create("test:product:2"));
    public static readonly ProductId Product3 = new(DeterministicGuid.Create("test:product:3"));
    public static readonly ProductId ScarceProduct = new(DeterministicGuid.Create("test:product:scarce"));

    /// <summary>Delivery address in Midtown Manhattan (~0.5 km from pharmacy 1).</summary>
    public static readonly AddressInput Address = new("11 E 36th St", "New York", "10016", 40.7493, -73.9822);

    public static UserId UserIdOf(string email) => new(DeterministicGuid.Create("test:user:" + email));

    public static CourierId CourierIdOf(string email) => new(DeterministicGuid.Create("test:courier:" + email));

    public static string ConcurrencyCustomer(int i) => $"customer{i}@test.local";
}
