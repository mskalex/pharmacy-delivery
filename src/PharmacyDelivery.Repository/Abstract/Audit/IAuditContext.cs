using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Abstract;

/// <summary>
/// Audit scope of the current request (scoped service). When active, every SaveChanges records old → new values
/// of changed properties in the same transaction (rolled back together with the business change).
/// </summary>
public interface IAuditContext
{
    /// <summary>Audit information of the current request, or null when the request is not audited.</summary>
    AuditInfo? Current { get; }

    /// <summary>Activates auditing for the rest of the request (called by the audit middleware).</summary>
    void Begin(AuditInfo info);
}
