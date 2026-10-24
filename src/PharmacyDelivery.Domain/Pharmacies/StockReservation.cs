using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Pharmacies;

public enum ReservationStatus
{
    Active,
    Committed,
    Released,
}

/// <summary>
/// Quantity of a product held for an order in a pharmacy. Unpaid reservations expire (<see cref="ExpiresAt"/>);
/// once the order is paid the expiry is cleared.
/// </summary>
public sealed class StockReservation : Entity<Guid>
{
    private StockReservation()
    {
    }

    public OrderId OrderId { get; private set; }

    public PharmacyId PharmacyId { get; private set; }

    public ProductId ProductId { get; private set; }

    public int Quantity { get; private set; }

    public ReservationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public static StockReservation Create(
        OrderId orderId,
        PharmacyId pharmacyId,
        ProductId productId,
        int quantity,
        DateTimeOffset now,
        DateTimeOffset? expiresAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrderId = orderId,
            PharmacyId = pharmacyId,
            ProductId = productId,
            Quantity = Guard.Positive(quantity),
            Status = ReservationStatus.Active,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

    public bool IsActive => Status == ReservationStatus.Active;

    public void ConfirmPaid() => ExpiresAt = null;

    public void Release(DateTimeOffset now) => Close(ReservationStatus.Released, now);

    public void Commit(DateTimeOffset now) => Close(ReservationStatus.Committed, now);

    private void Close(ReservationStatus status, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active)
            throw new InvalidOperationException($"Reservation {Id} is already {Status}.");

        Status = status;
        ClosedAt = now;
        ExpiresAt = null;
    }
}
