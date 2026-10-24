namespace PharmacyDelivery.Repository.Models;

public sealed record RevokedToken(Guid Jti, DateTimeOffset ExpiresAt);
