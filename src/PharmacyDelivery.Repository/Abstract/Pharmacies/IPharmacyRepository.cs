using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Pharmacies;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Access to pharmacy aggregates (read without tracking — pharmacies are not modified by use cases).</summary>
public interface IPharmacyRepository
{
    /// <summary>Loads a pharmacy, or null.</summary>
    Task<Pharmacy?> GetAsync(PharmacyId id, CancellationToken cancellationToken);

    /// <summary>Loads several pharmacies (opening hours are evaluated in memory).</summary>
    Task<IReadOnlyList<Pharmacy>> GetByIdsAsync(IReadOnlyCollection<PharmacyId> ids, CancellationToken cancellationToken);

    /// <summary>Identifiers of all active pharmacies.</summary>
    Task<IReadOnlyList<PharmacyId>> GetActiveIdsAsync(CancellationToken cancellationToken);
}
