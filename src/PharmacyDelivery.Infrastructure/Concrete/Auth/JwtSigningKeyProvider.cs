using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PharmacyDelivery.Services.Options;

namespace PharmacyDelivery.Infrastructure.Concrete;

/// <summary>
/// ES256 signing key. Loaded from <c>Auth:Jwt:SigningKeyPem</c> or <c>Auth:Jwt:SigningKeyPath</c>; an ephemeral key
/// is generated only when <c>AllowEphemeralSigningKey</c> is on (Development). The public part is exposed as JWKS,
/// so another service (e.g. an extracted Tracking service) can validate tokens without the private key.
/// </summary>
public sealed class JwtSigningKeyProvider : IDisposable
{
    private readonly ECDsa _ecdsa;

    public JwtSigningKeyProvider(IOptions<AuthOptions> options, ILogger<JwtSigningKeyProvider> logger)
    {
        var jwt = options.Value.Jwt;
        _ecdsa = ECDsa.Create();
        var pem = jwt.SigningKeyPem;
        if (string.IsNullOrWhiteSpace(pem) && !string.IsNullOrWhiteSpace(jwt.SigningKeyPath) && File.Exists(jwt.SigningKeyPath))
            pem = File.ReadAllText(jwt.SigningKeyPath);

        if (!string.IsNullOrWhiteSpace(pem))
        {
            _ecdsa.ImportFromPem(pem);
        }
        else if (jwt.AllowEphemeralSigningKey)
        {
            _ecdsa.GenerateKey(ECCurve.NamedCurves.nistP256);
            logger.LogWarning("No JWT signing key configured: using an ephemeral key (tokens die with the process). Development only");
        }
        else
        {
            throw new InvalidOperationException(
                "JWT signing key is not configured. Set Auth__Jwt__SigningKeyPem or Auth__Jwt__SigningKeyPath (see docs/configuration.md).");
        }

        SigningKey = new ECDsaSecurityKey(_ecdsa) { KeyId = jwt.KeyId };
        var publicKey = ECDsa.Create(_ecdsa.ExportParameters(includePrivateParameters: false));
        ValidationKey = new ECDsaSecurityKey(publicKey) { KeyId = jwt.KeyId };
        PublicJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(ValidationKey);
    }

    public ECDsaSecurityKey SigningKey { get; }

    public ECDsaSecurityKey ValidationKey { get; }

    public JsonWebKey PublicJwk { get; }

    public SigningCredentials Credentials => new(SigningKey, SecurityAlgorithms.EcdsaSha256);

    public void Dispose() => _ecdsa.Dispose();
}
