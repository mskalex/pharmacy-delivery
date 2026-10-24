namespace PharmacyDelivery.Repository.Auditing;

/// <summary>One audited SaveChanges: who, which API action, when. Written in the same transaction as the change.</summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string Action { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public string? Role { get; set; }

    public string HttpMethod { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? TraceId { get; set; }

    public List<AuditChange> Changes { get; set; } = [];
}

/// <summary>Old → new value of one property of one entity.</summary>
public sealed class AuditChange
{
    public long Id { get; set; }

    public Guid AuditEntryId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary><c>Added</c>, <c>Modified</c> or <c>Deleted</c>.</summary>
    public string ChangeType { get; set; } = string.Empty;

    public string Property { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }
}
