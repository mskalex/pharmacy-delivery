using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Domain.Catalog;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Infrastructure.Abstract;
using PharmacyDelivery.Infrastructure.Options;
using PharmacyDelivery.Infrastructure.Seed;
using PharmacyDelivery.Repository;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// Load-test volume ("large"): ~200 pharmacies, 5 000 products, ~1M stock rows, 10 000 customers, 100 000 orders
/// and 1M GPS points — so the EXPLAIN ANALYZE plans in docs/database.md are measured on realistic data.
/// Deterministic (fixed Random seed, no external generator library); bulk rows are written with binary COPY.
/// </summary>
[SeedProfile("large", Order = 20)]
public sealed class LargeDataSeeder(
    PharmacyDeliveryDbContext db,
    SeedPasswordHasher passwords,
    IClock clock,
    IOptions<SeedOptions> options,
    ILogger<LargeDataSeeder> logger) : ISeeder
{
    private const int PharmacyCount = 200;
    private const int ProductCount = 5_000;
    private const int CustomerCount = 10_000;
    private const int OrderCount = 100_000;
    private const int GpsPointCount = 1_000_000;
    private const int CourierSeries = 50;

    // New York City bounding box (about the same area as the original measurements: ~29 x 24 km).
    private const double MinLat = 40.60;
    private const double MaxLat = 40.86;
    private const double MinLng = -74.06;
    private const double MaxLng = -73.78;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var random = new Random(20261021);
        var now = clock.UtcNow;
        var categories = Enum.GetValues<ProductCategory>();

        var pharmacyIds = new List<PharmacyId>(PharmacyCount);
        for (var i = 0; i < PharmacyCount; i++)
        {
            var id = new PharmacyId(DeterministicGuid.Create("large:pharmacy:" + i));
            pharmacyIds.Add(id);
            db.Pharmacies.Add(Pharmacy.Create(
                id,
                $"L-{i:D4}",
                $"Test pharmacy {i}",
                $"{i + 1} Test Street",
                "New York",
                "10001",
                RandomPoint(random),
                i % 5 == 0
                    ? OpeningHours.NonStop()
                    : OpeningHours.Weekly(Enum.GetValues<DayOfWeek>()
                        .Select(d => new DailyOpeningHours(d, new TimeOnly(8, 0), new TimeOnly(21, 0))))));
        }

        var productIds = new List<ProductId>(ProductCount);
        for (var i = 0; i < ProductCount; i++)
        {
            var id = new ProductId(DeterministicGuid.Create("large:product:" + i));
            productIds.Add(id);
            db.Products.Add(Product.Create(
                id,
                $"Test product {i} {Words[i % Words.Length]}",
                SeedKeys.Ean(1_000_000 + i),
                categories[i % categories.Length],
                "Generated data for performance tests.",
                Money.OfUsd(5 + (i % 200))));
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        logger.LogInformation("Large seed: pharmacies and products inserted");

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);

        // Bulk COPY of a million rows legitimately exceeds the API statement timeout: lift it for this session only
        // (the session state is reset when the connection returns to the pool).
        await db.Database.ExecuteSqlRawAsync("SET statement_timeout = 0", cancellationToken);

        await CopyAsync(connection, "COPY stock_items (pharmacy_id, product_id, on_hand, reserved, updated_at) FROM STDIN (FORMAT BINARY)",
            async importer =>
            {
                foreach (var pharmacy in pharmacyIds)
                {
                    foreach (var product in productIds)
                    {
                        await importer.StartRowAsync(cancellationToken);
                        await importer.WriteAsync(pharmacy.Value, NpgsqlDbType.Uuid, cancellationToken);
                        await importer.WriteAsync(product.Value, NpgsqlDbType.Uuid, cancellationToken);
                        await importer.WriteAsync(random.Next(0, 60), NpgsqlDbType.Integer, cancellationToken);
                        await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
                        await importer.WriteAsync(now, NpgsqlDbType.TimestampTz, cancellationToken);
                    }
                }
            }, cancellationToken);
        logger.LogInformation("Large seed: {Rows} stock rows copied", PharmacyCount * ProductCount);

        var password = SeedKeys.FirstNonEmpty(options.Value.UsersPassword, options.Value.AdminPassword)
                       ?? throw new InvalidOperationException("Seed:UsersPassword must be configured for the large profile.");
        var hash = passwords.Hash(password, "customer@example.com", "Test customer");
        var customerIds = Enumerable.Range(0, CustomerCount).Select(i => DeterministicGuid.Create("large:customer:" + i)).ToList();
        await CopyAsync(connection,
            "COPY users (id, email, normalized_email, display_name, role, password_hash, failed_login_attempts, is_blocked, created_at, password_changed_at) FROM STDIN (FORMAT BINARY)",
            async importer =>
            {
                for (var i = 0; i < CustomerCount; i++)
                {
                    var email = $"customer{i}@example.com";
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(customerIds[i], NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(email, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(email, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync($"Test customer {i}", NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(Roles.Customer, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(hash, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(0, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
                    await importer.WriteAsync(now, NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(now, NpgsqlDbType.TimestampTz, cancellationToken);
                }
            }, cancellationToken);

        var orderIds = new Guid[OrderCount];
        var orderProducts = new int[OrderCount];
        await CopyAsync(connection,
            "COPY orders (id, number, customer_id, pharmacy_id, status, idempotency_key, created_at, updated_at, paid_at, delivered_at, estimated_delivery_at, rejected_pharmacy_ids, total_amount, total_currency, delivery_address) FROM STDIN (FORMAT BINARY)",
            async importer =>
            {
                for (var i = 0; i < OrderCount; i++)
                {
                    var created = now.AddMinutes(-random.Next(1, 60 * 24 * 90));
                    var productIndex = random.Next(ProductCount);
                    var point = RandomPoint(random);
                    orderIds[i] = DeterministicGuid.Create("large:order:" + i);
                    orderProducts[i] = productIndex;
                    var address = JsonSerializer.Serialize(
                        new { Street = "1 Test Street", City = "New York", PostalCode = "10001", point.Latitude, point.Longitude },
                        PharmacyDeliveryJsonOptions.Tolerant);

                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(orderIds[i], NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync("PD-L-" + i.ToString("D7", CultureInfo.InvariantCulture), NpgsqlDbType.Varchar,
                        cancellationToken);
                    await importer.WriteAsync(customerIds[i % CustomerCount], NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(pharmacyIds[i % PharmacyCount].Value, NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync("Delivered", NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync("large-" + i, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(created, NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(created.AddMinutes(35), NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(created.AddMinutes(1), NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(created.AddMinutes(35), NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(created.AddMinutes(35), NpgsqlDbType.TimestampTz, cancellationToken);
                    await importer.WriteAsync(Array.Empty<Guid>(), NpgsqlDbType.Array | NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(5m + (productIndex % 200), NpgsqlDbType.Numeric, cancellationToken);
                    await importer.WriteAsync(Money.Usd, NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(address, NpgsqlDbType.Jsonb, cancellationToken);
                }
            }, cancellationToken);

        await CopyAsync(connection,
            "COPY order_items (id, order_id, product_id, product_name, quantity, unit_price_amount, unit_price_currency) FROM STDIN (FORMAT BINARY)",
            async importer =>
            {
                for (var i = 0; i < OrderCount; i++)
                {
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(DeterministicGuid.Create("large:item:" + i), NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(orderIds[i], NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(productIds[orderProducts[i]].Value, NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync($"Test product {orderProducts[i]}", NpgsqlDbType.Varchar, cancellationToken);
                    await importer.WriteAsync(1, NpgsqlDbType.Integer, cancellationToken);
                    await importer.WriteAsync(5m + (orderProducts[i] % 200), NpgsqlDbType.Numeric, cancellationToken);
                    await importer.WriteAsync(Money.Usd, NpgsqlDbType.Varchar, cancellationToken);
                }
            }, cancellationToken);
        logger.LogInformation("Large seed: {Customers} customers and {Orders} orders copied", CustomerCount, OrderCount);

        await CopyAsync(connection,
            "COPY courier_locations (courier_id, delivery_id, latitude, longitude, speed_kmh, recorded_at) FROM STDIN (FORMAT BINARY)",
            async importer =>
            {
                for (var i = 0; i < GpsPointCount; i++)
                {
                    var point = RandomPoint(random);
                    await importer.StartRowAsync(cancellationToken);
                    await importer.WriteAsync(DeterministicGuid.Create("large:courier:" + (i % CourierSeries)), NpgsqlDbType.Uuid,
                        cancellationToken);
                    await importer.WriteAsync(orderIds[i % OrderCount], NpgsqlDbType.Uuid, cancellationToken);
                    await importer.WriteAsync(point.Latitude, NpgsqlDbType.Double, cancellationToken);
                    await importer.WriteAsync(point.Longitude, NpgsqlDbType.Double, cancellationToken);
                    await importer.WriteAsync(20.0 + (i % 15), NpgsqlDbType.Double, cancellationToken);
                    await importer.WriteAsync(now.AddSeconds(-(i % (86_400 * 7))), NpgsqlDbType.TimestampTz, cancellationToken);
                }
            }, cancellationToken);
        logger.LogInformation("Large seed: {Points} GPS points copied", GpsPointCount);

        await db.Database.ExecuteSqlRawAsync("ANALYZE", cancellationToken);
    }

    private static readonly string[] Words = ["forte", "max", "junior", "plus", "control", "extra", "duo", "mini"];

    private static GeoPoint RandomPoint(Random random) =>
        new(MinLat + (random.NextDouble() * (MaxLat - MinLat)), MinLng + (random.NextDouble() * (MaxLng - MinLng)));

    private static async Task CopyAsync(NpgsqlConnection connection, string command, Func<NpgsqlBinaryImporter, Task> write,
        CancellationToken cancellationToken)
    {
        await using var importer = await connection.BeginBinaryImportAsync(command, cancellationToken);
        await write(importer);
        await importer.CompleteAsync(cancellationToken);
    }
}
