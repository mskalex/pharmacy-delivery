using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>Read access to the audit log (old → new values).</summary>
public interface IAuditQueries
{
    /// <summary>Audit entries, newest first, filtered by entity and/or user.</summary>
    Task<PagedResult<AuditEntryView>> SearchAsync(
        string? entityType,
        string? entityId,
        Guid? userId,
        PageRequest page,
        CancellationToken cancellationToken);
}
