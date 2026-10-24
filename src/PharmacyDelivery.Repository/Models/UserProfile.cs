namespace PharmacyDelivery.Repository.Models;

public sealed record UserProfile(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    Guid? PharmacyId,
    Guid? CourierId,
    IReadOnlyList<AddressInput> Addresses);
