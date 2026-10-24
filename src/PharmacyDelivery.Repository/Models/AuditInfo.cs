namespace PharmacyDelivery.Repository.Models;

/// <summary>Who did what: set by the API audit middleware for endpoints marked with <c>[Audited]</c>.</summary>
public sealed record AuditInfo(string Action, Guid? UserId, string? Role, string HttpMethod, string Path, string? TraceId);
