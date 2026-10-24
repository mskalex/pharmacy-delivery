using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Guards;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Domain.Deliveries;

public enum CourierStatus
{
    Offline,
    Available,
    Busy,
}

public sealed class Courier : AggregateRoot<CourierId>
{
    private Courier()
    {
    }

    public UserId UserId { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public CourierStatus Status { get; private set; }

    public static Courier Create(CourierId id, UserId userId, string displayName) =>
        new()
        {
            Id = id,
            UserId = userId,
            DisplayName = Guard.NotBlank(displayName),
            Status = CourierStatus.Available,
        };

    public void MarkBusy()
    {
        if (Status == CourierStatus.Busy)
            throw new DomainException(ErrorCodes.CourierBusy, $"Courier {Id} already has an active delivery.");

        Status = CourierStatus.Busy;
    }

    public void MarkAvailable() => Status = CourierStatus.Available;

    public void GoOffline() => Status = CourierStatus.Offline;
}
