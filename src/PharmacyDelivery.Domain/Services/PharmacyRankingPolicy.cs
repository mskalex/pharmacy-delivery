using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Services;

/// <summary>Pharmacy that can fulfil the whole basket, with inputs for ranking.</summary>
public sealed record PharmacyCandidate(PharmacyId PharmacyId, string Name, double DistanceKm, TimeSpan TravelTime, int ActiveOrders);

/// <summary>
/// Ranks pharmacies by expected delivery time: travel time plus the time needed to work through the pharmacy's
/// current queue. Distance alone is not enough — a slightly further but idle pharmacy usually delivers sooner.
/// </summary>
public sealed class PharmacyRankingPolicy(TimeSpan perQueuedOrder, TimeSpan basePreparationTime)
{
    public static PharmacyRankingPolicy Default { get; } = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5));

    public TimeSpan EstimateDelivery(PharmacyCandidate candidate) =>
        basePreparationTime + (perQueuedOrder * candidate.ActiveOrders) + candidate.TravelTime;

    public IReadOnlyList<PharmacyCandidate> Rank(IEnumerable<PharmacyCandidate> candidates, IReadOnlyCollection<Guid> excluded) =>
        candidates
            .Where(c => !excluded.Contains(c.PharmacyId.Value))
            .OrderBy(EstimateDelivery)
            .ThenBy(c => c.DistanceKm)
            .ThenBy(c => c.PharmacyId.Value)
            .ToList();
}
