using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Domain.Identity;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

public sealed class JwtAccessTokenIssuer(JwtSigningKeyProvider keys, IOptions<AuthOptions> options) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();
    private readonly AuthOptions _options = options.Value;

    public IssuedAccessToken Issue(User user, Guid sessionId, DateTimeOffset now)
    {
        var jti = Guid.CreateVersion7();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [ClaimNames.Subject] = user.Id.Value.ToString(),
            [ClaimNames.Role] = user.Role,
            [ClaimNames.Email] = user.Email,
            [ClaimNames.Name] = user.DisplayName,
            [ClaimNames.SessionId] = sessionId.ToString(),
            [ClaimNames.TokenId] = jti.ToString(),
        };
        if (user.PharmacyId is { } pharmacyId)
            claims[ClaimNames.PharmacyId] = pharmacyId.Value.ToString();

        if (user.CourierId is { } courierId)
            claims[ClaimNames.CourierId] = courierId.Value.ToString();

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = keys.Credentials,
        });

        return new IssuedAccessToken(token, jti, expires);
    }
}
