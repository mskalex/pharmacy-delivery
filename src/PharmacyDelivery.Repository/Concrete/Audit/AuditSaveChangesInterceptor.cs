using System.Collections;
using System.Globalization;
using System.Text.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Abstract;
using PharmacyDelivery.Common.Serialization;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Auditing;
using PharmacyDelivery.Repository.Models;
using PharmacyDelivery.Repository.Options;

namespace PharmacyDelivery.Repository.Concrete;

/// <summary>
/// Records old → new values of every changed property when the request is audited (endpoint marked with
/// <c>[Audited]</c>). Audit rows are added to the same SaveChanges, so they commit or roll back together with the
/// business change. Secrets (password and token hashes) are masked.
/// </summary>
public sealed class AuditSaveChangesInterceptor(
    IAuditContext auditContext,
    IClock clock,
    IOptions<AuditOptions> options) : SaveChangesInterceptor
{
    private const string Masked = "***";
    private readonly AuditOptions _options = options.Value;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_options.Enabled && auditContext.Current is { } info && eventData.Context is { } context)
            AddAuditEntry(context, info);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntry(DbContext context, AuditInfo info)
    {
        var changes = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => !IsInfrastructure(e.Metadata.ClrType))
            .SelectMany(Describe)
            .ToList();

        if (changes.Count == 0)
            return;

        var entry = new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = clock.UtcNow,
            Action = info.Action,
            UserId = info.UserId,
            Role = info.Role,
            HttpMethod = info.HttpMethod,
            Path = info.Path.Length > 300 ? info.Path[..300] : info.Path,
            TraceId = info.TraceId,
            Changes = changes,
        };

        context.Add(entry);
    }

    private IEnumerable<AuditChange> Describe(EntityEntry entry)
    {
        var entityType = entry.Metadata.ClrType.Name;
        var entityId = FormatKey(entry);
        var changeType = entry.State.ToString();

        var properties = entry.Properties
            .Where(p => !p.Metadata.IsShadowProperty() || p.Metadata.IsForeignKey())
            .Select(p => (Name: p.Metadata.Name, p.OriginalValue, p.CurrentValue, p.IsModified))
            .Concat(entry.ComplexProperties.SelectMany(c => c.Properties.Select(p =>
                (Name: c.Metadata.Name + "." + p.Metadata.Name, p.OriginalValue, p.CurrentValue, p.IsModified))));

        foreach (var (name, original, current, isModified) in properties)
        {
            if (name == "xmin")
                continue;

            var masked = _options.MaskedProperties.Contains(name, StringComparer.OrdinalIgnoreCase);
            switch (entry.State)
            {
                case EntityState.Added:
                    yield return Change(entityType, entityId, changeType, name, null, masked ? Masked : Format(current));
                    break;
                case EntityState.Deleted:
                    yield return Change(entityType, entityId, changeType, name, masked ? Masked : Format(original), null);
                    break;
                case EntityState.Modified when isModified:
                    var oldValue = Format(original);
                    var newValue = Format(current);
                    if (oldValue != newValue)
                        yield return Change(entityType, entityId, changeType, name, masked ? Masked : oldValue, masked ? Masked : newValue);
                    break;
            }
        }
    }

    private static AuditChange Change(string entityType, string entityId, string changeType, string property, string? oldValue,
        string? newValue) =>
        new()
        {
            EntityType = entityType,
            EntityId = entityId,
            ChangeType = changeType,
            Property = property,
            OldValue = Truncate(oldValue),
            NewValue = Truncate(newValue),
        };

    private static string FormatKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return string.Empty;

        return string.Join("|", key.Properties.Select(p => Format(entry.Property(p.Name).CurrentValue)));
    }

    private static string? Format(object? value) =>
        value switch
        {
            null => null,
            string s => s,
            DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
            DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
            decimal m => m.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            Enum e => e.ToString(),
            IFormattable f when value.GetType().IsPrimitive => f.ToString(null, CultureInfo.InvariantCulture),
            IEnumerable and not string => JsonSerializer.Serialize(value, PharmacyDeliveryJsonOptions.Tolerant),
            _ => value.ToString(),
        };

    private static string? Truncate(string? value) => value is { Length: > 4000 } ? value[..4000] : value;

    private static bool IsInfrastructure(Type type) =>
        type == typeof(AuditEntry)
        || type == typeof(AuditChange)
        || type.Namespace?.StartsWith("MassTransit", StringComparison.Ordinal) == true
        || typeof(InboxState).IsAssignableFrom(type);
}
