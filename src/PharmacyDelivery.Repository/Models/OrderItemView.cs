namespace PharmacyDelivery.Repository.Models;

public sealed record OrderItemView(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);
