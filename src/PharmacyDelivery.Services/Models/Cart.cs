namespace PharmacyDelivery.Services.Models;

/// <summary>Cart payload. <see cref="SchemaVersion"/> allows migrating the stored format when it changes.</summary>
public sealed class Cart
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<CartItem> Items { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }
}
