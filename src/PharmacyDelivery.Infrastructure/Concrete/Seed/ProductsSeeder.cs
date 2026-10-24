using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Infrastructure.Concrete;

[SeedProfile("demo", Order = 30)]
public sealed class ProductsSeeder(PharmacyDeliveryDbContext db, SeedDataSource data) : ISeeder
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var products = data.Read<List<ProductJson>>("products.json");
        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            db.Products.Add(Product.Create(SeedKeys.Product(i), p.Name, SeedKeys.Ean(i), p.Category, p.Description, Money.OfUsd(p.Price)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
