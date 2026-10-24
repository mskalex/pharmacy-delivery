using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Domain.Abstract;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Pharmacies;

/// <summary>
/// Stock of one product in one pharmacy. Invariant: <c>0 ≤ Reserved ≤ OnHand</c>.
/// Rows are locked with <c>SELECT … FOR UPDATE</c> (ordered by product id) before any mutation, see DESIGN.md §10.
/// </summary>
public sealed class StockItem : IAggregateRoot
{
    private StockItem()
    {
    }

    public PharmacyId PharmacyId { get; private set; }

    public ProductId ProductId { get; private set; }

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int Available => OnHand - Reserved;

    public IReadOnlyCollection<IDomainEvent> DomainEvents => [];

    public static StockItem Create(PharmacyId pharmacyId, ProductId productId, int onHand, DateTimeOffset now) =>
        new()
        {
            PharmacyId = pharmacyId,
            ProductId = productId,
            OnHand = Guard.NotNegative(onHand),
            Reserved = 0,
            UpdatedAt = now,
        };

    public void ClearDomainEvents()
    {
    }

    public bool CanReserve(int quantity) => quantity > 0 && Available >= quantity;

    public void Reserve(int quantity, DateTimeOffset now)
    {
        Guard.Positive(quantity);
        if (Available < quantity)
        {
            throw new DomainException(
                ErrorCodes.StockInsufficient,
                $"Insufficient stock of product {ProductId}: requested {quantity}, available {Available}.");
        }

        Reserved += quantity;
        UpdatedAt = now;
    }

    public void Release(int quantity, DateTimeOffset now)
    {
        Guard.Positive(quantity);
        Reserved = Math.Max(0, Reserved - quantity);
        UpdatedAt = now;
    }

    /// <summary>Goods physically left the shelf: both reserved and on-hand quantities decrease.</summary>
    public void Commit(int quantity, DateTimeOffset now)
    {
        Guard.Positive(quantity);
        if (Reserved < quantity || OnHand < quantity)
        {
            throw new DomainException(
                ErrorCodes.StockInvariantViolated,
                $"Cannot commit {quantity} of product {ProductId}: reserved {Reserved}, on hand {OnHand}.");
        }

        Reserved -= quantity;
        OnHand -= quantity;
        UpdatedAt = now;
    }

    /// <summary>
    /// Applies a stock count coming from the pharmacy system. If the counted quantity is lower than what is already
    /// reserved, on-hand is kept at the reserved level (invariant) and the deficit is returned so it can be reported.
    /// </summary>
    public int ApplyStockCount(int counted, DateTimeOffset now)
    {
        Guard.NotNegative(counted);
        var deficit = Math.Max(0, Reserved - counted);
        OnHand = Math.Max(counted, Reserved);
        UpdatedAt = now;
        return deficit;
    }
}
