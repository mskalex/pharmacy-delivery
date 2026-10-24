using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Infrastructure.Concrete;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Public signing keys (JWKS) for services that validate our access tokens.</summary>
[ApiController]
[Route(".well-known")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class JwksController : ControllerBase
{
    /// <summary>JSON Web Key Set with the public ES256 key; <c>kid</c> matches the header of issued access tokens.</summary>
    [HttpGet("jwks.json")]
    [AllowAnonymous]
    public IActionResult Jwks([FromServices] JwtSigningKeyProvider keys) =>
        Ok(new
        {
            keys = new[]
            {
                new
                {
                    kty = keys.PublicJwk.Kty, crv = keys.PublicJwk.Crv, x = keys.PublicJwk.X, y = keys.PublicJwk.Y,
                    kid = keys.PublicJwk.Kid, use = "sig", alg = "ES256"
                },
            },
        });
}
