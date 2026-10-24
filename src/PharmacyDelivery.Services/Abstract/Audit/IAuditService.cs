using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Use cases of the audit log.</summary>
public interface IAuditService
{
    /// <summary>Audit entries filtered by entity type/id and user, newest first.</summary>
    Task<PagedResult<AuditEntryView>> SearchAsync(string? entityType, string? entityId, Guid? userId, PageRequest page,
        CancellationToken cancellationToken);
}
