using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Services.Concrete;

/// <summary>Read access to the audit log (old → new values recorded for [Audited] API actions).</summary>
public sealed class AuditService(IAuditQueries queries) : IAuditService
{
    public Task<PagedResult<AuditEntryView>> SearchAsync(string? entityType, string? entityId, Guid? userId, PageRequest page,
        CancellationToken cancellationToken) =>
        queries.SearchAsync(entityType, entityId, userId, page, cancellationToken);
}
