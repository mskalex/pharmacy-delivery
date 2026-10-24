namespace PharmacyDelivery.Services.Models;

public sealed record PharmacySelection(IReadOnlyList<RankedPharmacy> Ranked, int PharmaciesInRange);
