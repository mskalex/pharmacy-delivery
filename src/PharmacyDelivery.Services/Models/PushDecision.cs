namespace PharmacyDelivery.Services.Models;

public readonly record struct PushDecision(bool Push, bool RecalculateEta, DateTimeOffset? CachedEta);
