namespace PharmacyDelivery.Services.Models;

public sealed record CartLineView(Guid ProductId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal, bool IsAvailable);
