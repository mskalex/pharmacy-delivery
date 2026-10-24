using PharmacyDelivery.Common.Abstract;

namespace PharmacyDelivery.Common.Concrete;

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
