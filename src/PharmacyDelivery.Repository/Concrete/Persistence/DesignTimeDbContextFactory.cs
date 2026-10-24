using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PharmacyDelivery.Repository.Options;

namespace PharmacyDelivery.Repository.Concrete;

/// <summary>Used by <c>dotnet ef migrations</c>; no database connection is opened at design time.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PharmacyDeliveryDbContext>
{
    public PharmacyDeliveryDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<PharmacyDeliveryDbContext>();
        PharmacyDeliveryDbContextConfiguration.Configure(
            builder,
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ??
            "Host=localhost;Database=pharmacy_delivery;Username=pharmacy_delivery;Password=design-time",
            new DatabaseOptions());
        return new PharmacyDeliveryDbContext(builder.Options);
    }
}
