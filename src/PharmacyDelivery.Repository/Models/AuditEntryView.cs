namespace PharmacyDelivery.Repository.Models;

public sealed record AuditEntryView(
    Guid Id,
    DateTimeOffset OccurredAt,
    string Action,
    Guid? UserId,
    string? Role,
    string HttpMethod,
    string Path,
    string? TraceId,
    IReadOnlyList<AuditChangeView> Changes);
