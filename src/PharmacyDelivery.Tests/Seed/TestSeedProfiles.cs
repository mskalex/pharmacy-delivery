namespace PharmacyDelivery.Tests.Seed;

/// <summary>
/// Names of the test data templates. A test chooses one with <c>[UseSeed(TestSeedProfiles.Minimal)]</c>; seeders join a template
/// with <c>[SeedProfile(TestSeedProfiles.Minimal, Order = …)]</c> — the same mechanism as the application templates
/// (<c>demo</c>, <c>empty</c>, <c>large</c>). Contents are described in docs/test-plan.md §5.
/// </summary>
public static class TestSeedProfiles
{
    /// <summary>Pharmacy T-1 (24/7), three products, two customers, a pharmacist, a courier, an admin.</summary>
    public const string Minimal = "minimal";

    /// <summary><see cref="Minimal"/> + a scarce product (10 units, T-1 only) + 50 customers.</summary>
    public const string Concurrency = "concurrency";

    /// <summary><see cref="Minimal"/> + pharmacy T-2 (~1 km) with products 1–2 and its pharmacist.</summary>
    public const string Reassign = "reassign";

    /// <summary><see cref="Minimal"/> + a courier 200 m and a courier ~3 km from T-1; the base courier's position is stale.</summary>
    public const string Dispatch = "dispatch";
}
