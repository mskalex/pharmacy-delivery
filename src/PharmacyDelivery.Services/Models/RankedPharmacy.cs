using PharmacyDelivery.Domain.Pharmacies;
using PharmacyDelivery.Domain.Services;

namespace PharmacyDelivery.Services.Models;

public sealed record RankedPharmacy(Pharmacy Pharmacy, PharmacyCandidate Candidate, TimeSpan EstimatedDelivery);
