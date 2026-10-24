namespace PharmacyDelivery.Repository.Models;

/// <summary>Pharmacy able to fulfil the whole basket (output of the PostGIS candidate query).</summary>
public sealed record PharmacyCandidateRow(Guid PharmacyId, string Name, double DistanceKm, int ActiveOrders);
