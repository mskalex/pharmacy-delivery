using Microsoft.EntityFrameworkCore;
using PharmacyDelivery.Domain.Common;
using PharmacyDelivery.Domain.Payments;
using PharmacyDelivery.Repository.Abstract;

namespace PharmacyDelivery.Repository.Concrete;

public sealed class PaymentRepository(PharmacyDeliveryDbContext db) : IPaymentRepository
{
    public void Add(Payment payment) => db.Payments.Add(payment);

    public Task<Payment?> FindByProviderIdAsync(string provider, string providerPaymentId, CancellationToken cancellationToken) =>
        db.Payments.FirstOrDefaultAsync(p => p.Provider == provider && p.ProviderPaymentId == providerPaymentId, cancellationToken);

    public Task<Payment?> FindSucceededForOrderAsync(OrderId orderId, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking()
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Succeeded)
            .OrderByDescending(p => p.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
