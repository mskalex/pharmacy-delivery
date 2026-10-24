namespace PharmacyDelivery.Repository.Models;

public sealed record AuditChangeView(
    string EntityType,
    string EntityId,
    string ChangeType,
    string Property,
    string? OldValue,
    string? NewValue);
