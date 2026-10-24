namespace PharmacyDelivery.Services.Models;

public sealed class CartItem
{
    public Guid ProductId { get; set; }

    public int Quantity { get; set; }
}
