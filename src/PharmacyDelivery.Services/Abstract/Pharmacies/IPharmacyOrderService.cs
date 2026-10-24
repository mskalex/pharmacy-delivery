using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Pharmacy panel use cases and reassignment of orders between pharmacies.</summary>
public interface IPharmacyOrderService
{
    /// <summary>Active orders of the pharmacy.</summary>
    Task<IReadOnlyList<PharmacyOrderSummary>> GetQueueAsync(Guid pharmacyId, CancellationToken cancellationToken);

    /// <summary>Consumer of <c>OrderPaid</c>: hands the paid order to its pharmacy (idempotent).</summary>
    Task AssignAfterPaymentAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>The pharmacy confirms it will prepare the order.</summary>
    Task AcceptAsync(Guid pharmacyId, Guid orderId, CancellationToken cancellationToken);

    /// <summary>The pharmacy rejects the order; it moves to the next pharmacy or is cancelled and refunded.</summary>
    Task RejectAsync(Guid pharmacyId, Guid orderId, string reason, CancellationToken cancellationToken);

    /// <summary>The order is packed: reserved stock is committed and a courier is dispatched.</summary>
    Task MarkReadyAsync(Guid pharmacyId, Guid orderId, CancellationToken cancellationToken);

    /// <summary>Reassigns orders not accepted in time. Returns the number of processed orders.</summary>
    Task<int> ProcessAcceptanceTimeoutsAsync(int batchSize, CancellationToken cancellationToken);
}
