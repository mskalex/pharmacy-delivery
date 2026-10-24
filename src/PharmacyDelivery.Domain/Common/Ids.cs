namespace PharmacyDelivery.Domain.Common;

// Strongly typed identifiers: a ProductId can never be passed where an OrderId is expected.
// They never leave the Application layer — DTOs and contracts use plain Guid.

public readonly record struct ProductId(Guid Value)
{
    public static ProductId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PharmacyId(Guid Value)
{
    public static PharmacyId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct CourierId(Guid Value)
{
    public static CourierId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct DeliveryId(Guid Value)
{
    public static DeliveryId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
