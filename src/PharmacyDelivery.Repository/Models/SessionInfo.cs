namespace PharmacyDelivery.Repository.Models;

public sealed record SessionInfo(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    bool IsCurrent);
