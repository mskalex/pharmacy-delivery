namespace PharmacyDelivery.Services.Abstract;

/// <summary>Current authenticated principal (from the JWT). Values are null for anonymous calls.</summary>
public interface ICurrentUser
{
    /// <summary>Whether the request carries a valid token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Identifier of the user (<c>sub</c>); throws for anonymous requests.</summary>
    Guid UserId { get; }

    /// <summary>Application role (<c>role</c> claim).</summary>
    string? Role { get; }

    /// <summary>Pharmacy of a pharmacist (<c>pharmacy_id</c> claim).</summary>
    Guid? PharmacyId { get; }

    /// <summary>Courier of a courier account (<c>courier_id</c> claim).</summary>
    Guid? CourierId { get; }

    /// <summary>Session of the token (<c>sid</c> claim).</summary>
    Guid? SessionId { get; }
}
