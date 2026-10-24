using System.Globalization;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Infrastructure.Seed;

// Demo data template ("demo"): 8 pharmacies in New York City, ~60 OTC products, stock, customers, pharmacists,
// couriers and a few historical orders. All names are fictional. Ids are deterministic (DeterministicGuid).
// Seeders write through DbContext directly: no domain events are dispatched and nothing is audited.

internal static class SeedKeys
{
    public static PharmacyId Pharmacy(string code) => new(DeterministicGuid.Create("pharmacy:" + code));

    public static ProductId Product(int index) => new(DeterministicGuid.Create("product:" + index));

    public static UserId User(string email) => new(DeterministicGuid.Create("user:" + email.ToLowerInvariant()));

    public static CourierId Courier(string email) => new(DeterministicGuid.Create("courier:" + email.ToLowerInvariant()));

    public static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>FNV-1a: stable across processes (string.GetHashCode is randomized per process).</summary>
    public static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
            hash = (hash ^ c) * 16777619u;

        return hash;
    }

    /// <summary>13-digit EAN (GTIN-13) with a valid check digit, GS1 prefix 003 (US).</summary>
    public static string Ean(int index)
    {
        var body = "003" + (100000000 + index).ToString(CultureInfo.InvariantCulture);
        var sum = body.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return body + ((10 - (sum % 10)) % 10);
    }
}
