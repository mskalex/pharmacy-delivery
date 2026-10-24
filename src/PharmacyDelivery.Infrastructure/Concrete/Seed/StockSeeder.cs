using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Stock rules (deterministic): most products are available everywhere; some are missing in some pharmacies
/// (so the pharmacy is chosen by basket completeness); the scarce product exists only in NYC-MID with 10 units
/// (concurrent reservation demo).
/// </summary>
[SeedProfile("demo", Order = 40)]
public sealed class StockSeeder(PharmacyDeliveryDbContext db, SeedDataSource data, IClock clock) : ISeeder
{
    public const string ScarceProductPrefix = "Children's cough syrup";

    /// <summary>Products missing in the Midtown pharmacy: a basket with them goes to a further pharmacy.</summary>
    private static readonly HashSet<int> MissingInCentre = [2, 21];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var pharmacies = data.Read<List<PharmacyJson>>("pharmacies.json");
        var products = data.Read<List<ProductJson>>("products.json");
        var now = clock.UtcNow;

        foreach (var pharmacy in pharmacies)
        {
            for (var i = 0; i < products.Count; i++)
            {
                var hash = SeedKeys.StableHash(pharmacy.Code + ":" + i);
                int quantity;
                if (products[i].Name.StartsWith(ScarceProductPrefix, StringComparison.Ordinal))
                    quantity = pharmacy.Code == "NYC-MID" ? 10 : 0;
                else if (pharmacy.Code == "NYC-MID" && MissingInCentre.Contains(i))
                    quantity = 0;
                else
                    quantity = hash % 9 == 0 ? 0 : 5 + (int)(hash % 36);

                if (quantity > 0)
                    db.StockItems.Add(StockItem.Create(SeedKeys.Pharmacy(pharmacy.Code), SeedKeys.Product(i), quantity, now));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
