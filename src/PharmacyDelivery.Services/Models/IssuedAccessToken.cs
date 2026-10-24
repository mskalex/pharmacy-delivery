namespace PharmacyDelivery.Services.Models;

public sealed record IssuedAccessToken(string Token, Guid Jti, DateTimeOffset ExpiresAt);
