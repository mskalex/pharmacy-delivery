using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Public list of partner pharmacies.</summary>
public interface IPharmacyDirectoryService
{
    /// <summary>Active pharmacies (cached).</summary>
    Task<IReadOnlyList<PharmacyInfo>> ListAsync(CancellationToken cancellationToken);
}
