using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Read side of pharmacies, including the PostGIS candidate search.</summary>
public interface IPharmacyQueries
{
    /// <summary>
    /// Active pharmacies within <paramref name="radiusKm"/> (PostGIS <c>ST_DWithin</c>, KNN order) that have every line
    /// available, with the number of orders currently in their queue.
    /// </summary>
    Task<IReadOnlyList<PharmacyCandidateRow>> FindCandidatesAsync(
        GeoPoint location,
        double radiusKm,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines,
        CancellationToken cancellationToken);

    /// <summary>Number of active pharmacies within the radius (distinguishes "out of zone" from "no stock").</summary>
    Task<int> CountWithinRadiusAsync(GeoPoint location, double radiusKm, CancellationToken cancellationToken);

    /// <summary>Name, address and location of a pharmacy, or null.</summary>
    Task<PharmacyInfo?> GetInfoAsync(Guid pharmacyId, CancellationToken cancellationToken);

    /// <summary>All active pharmacies sorted by name.</summary>
    Task<IReadOnlyList<PharmacyInfo>> ListAsync(CancellationToken cancellationToken);
}
