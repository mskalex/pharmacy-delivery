using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class AuditContext : IAuditContext
{
    public AuditInfo? Current { get; private set; }

    public void Begin(AuditInfo info) => Current = info;
}
