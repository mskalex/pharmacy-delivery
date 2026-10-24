using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Services.Abstract;

/// <summary>Issues signed access tokens (JWT ES256) for the local identity provider mode.</summary>
public interface IAccessTokenIssuer
{
    /// <summary>Creates a token for the user's session; the returned <c>jti</c> must be registered for revocation.</summary>
    IssuedAccessToken Issue(User user, Guid sessionId, DateTimeOffset now);
}
