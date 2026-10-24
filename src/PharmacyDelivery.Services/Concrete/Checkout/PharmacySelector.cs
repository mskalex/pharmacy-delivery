using Microsoft.Extensions.Options;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Services;
using PharmacyDelivery.Domain.Shared;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Diagnostics;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Services.Concrete;

public sealed class PharmacySelector(
    IPharmacyQueries pharmacyQueries,
    IPharmacyRepository pharmacies,
    IEtaService eta,
    IOptions<CheckoutOptions> checkoutOptions,
    IOptions<PharmacyOptions> pharmacyOptions) : IPharmacySelector
{
    private readonly CheckoutOptions _checkout = checkoutOptions.Value;

    private readonly PharmacyRankingPolicy _policy = new(
        TimeSpan.FromMinutes(pharmacyOptions.Value.MinutesPerQueuedOrder),
        TimeSpan.FromMinutes(pharmacyOptions.Value.BasePreparationMinutes));

    public async Task<PharmacySelection> SelectAsync(
        GeoPoint destination,
        IReadOnlyCollection<(ProductId ProductId, int Quantity)> lines,
        IReadOnlyCollection<Guid> excludedPharmacyIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var activity = PharmacyDeliveryTelemetry.StartActivity("pharmacy.select");

        var rows = await pharmacyQueries.FindCandidatesAsync(
            destination,
            _checkout.DeliveryRadiusKm,
            lines.Select(l => (l.ProductId.Value, l.Quantity)).ToList(),
            cancellationToken);

        var inRange = rows.Count > 0
            ? rows.Count
            : await pharmacyQueries.CountWithinRadiusAsync(destination, _checkout.DeliveryRadiusKm, cancellationToken);

        var eligibleRows = rows.Where(r => !excludedPharmacyIds.Contains(r.PharmacyId)).ToList();
        if (eligibleRows.Count == 0)
        {
            activity?.SetTag("candidates", 0);
            return new PharmacySelection([], inRange);
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_checkout.TimeZone);
        var entities = (await pharmacies.GetByIdsAsync(eligibleRows.Select(r => new PharmacyId(r.PharmacyId)).ToList(), cancellationToken))
            .Where(p => p.IsOpenAt(now, timeZone))
            .ToDictionary(p => p.Id.Value);

        // Travel times are requested concurrently; the ETA service limits concurrency itself (SemaphoreSlim).
        var candidates = await Task.WhenAll(eligibleRows
            .Where(r => entities.ContainsKey(r.PharmacyId))
            .Select(async r =>
            {
                var pharmacy = entities[r.PharmacyId];
                var travel = await eta.EstimateTravelTimeAsync(pharmacy.Location, destination, cancellationToken);
                return new PharmacyCandidate(pharmacy.Id, pharmacy.Name, r.DistanceKm, travel, r.ActiveOrders);
            }));

        var ranked = _policy.Rank(candidates, excludedPharmacyIds)
            .Select(c => new RankedPharmacy(entities[c.PharmacyId.Value], c, _policy.EstimateDelivery(c)))
            .ToList();

        activity?.SetTag("candidates", ranked.Count);
        return new PharmacySelection(ranked, inRange);
    }
}
