namespace PharmacyDelivery.Services.Models;

// Results and inputs of services that do not come from the database read side.

public sealed record CartView(IReadOnlyList<CartLineView> Lines, decimal Total, string Currency, DateTimeOffset? ExpiresAt);
