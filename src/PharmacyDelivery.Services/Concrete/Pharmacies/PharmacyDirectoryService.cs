using Microsoft.Extensions.Caching.Hybrid;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>Public list of partner pharmacies (rarely changes: cached for 10 minutes).</summary>
public sealed class PharmacyDirectoryService(IPharmacyQueries queries, HybridCache cache) : IPharmacyDirectoryService
{
    public async Task<IReadOnlyList<PharmacyInfo>> ListAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(
            "pharmacies:all",
            queries,
            static async (q, ct) => await q.ListAsync(ct),
            new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(10), LocalCacheExpiration = TimeSpan.FromMinutes(1) },
            ["pharmacies"],
            cancellationToken);
}
