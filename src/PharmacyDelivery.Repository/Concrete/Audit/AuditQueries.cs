using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Common.Paging;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class AuditQueries(PharmacyDeliveryDbContext db) : IAuditQueries
{
    public async Task<PagedResult<AuditEntryView>> SearchAsync(
        string? entityType,
        string? entityId,
        Guid? userId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = db.AuditEntries.AsNoTracking();
        if (userId is not null)
            query = query.Where(e => e.UserId == userId);

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(e => e.Changes.Any(c =>
                c.EntityType == entityType && (string.IsNullOrEmpty(entityId) || c.EntityId == entityId)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var entries = await query
            .OrderByDescending(e => e.OccurredAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Include(e => e.Changes)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var items = entries
            .Select(e => new AuditEntryView(
                e.Id,
                e.OccurredAt,
                e.Action,
                e.UserId,
                e.Role,
                e.HttpMethod,
                e.Path,
                e.TraceId,
                e.Changes
                    .OrderBy(c => c.Id)
                    .Select(c => new AuditChangeView(c.EntityType, c.EntityId, c.ChangeType, c.Property, c.OldValue, c.NewValue))
                    .ToList()))
            .ToList();

        return new PagedResult<AuditEntryView>(items, page.Page, page.PageSize, total);
    }
}
