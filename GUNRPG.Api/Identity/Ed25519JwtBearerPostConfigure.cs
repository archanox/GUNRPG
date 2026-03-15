using GUNRPG.Application.Identity;
using GUNRPG.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc8032;
using System.Text;

namespace GUNRPG.Api.Identity;

/// <summary>
/// Configures JwtBearer to validate Ed25519-signed tokens using the server's public key.
/// Wired up as <see cref="IPostConfigureOptions{TOptions}"/> so it can resolve the
/// <see cref="JwtTokenService"/> singleton after DI is fully built.
/// </summary>
public sealed class Ed25519JwtBearerPostConfigure : IPostConfigureOptions<JwtBearerOptions>
{
    private readonly IServiceProvider _sp;

    public Ed25519JwtBearerPostConfigure(IServiceProvider sp) => _sp = sp;

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        var jwtOptions = _sp.GetRequiredService<IOptions<JwtOptions>>().Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = false,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            SignatureValidator = (token, _) => ValidateEd25519(token),
        };
    }

    private SecurityToken ValidateEd25519(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3)
            throw new SecurityTokenInvalidSignatureException("Malformed JWT: expected 3 dot-separated parts.");

        var publicKeyBytes = _sp.GetRequiredService<IPublicKeyProvider>().GetPublicKeyBytes();
        var data = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlDecode(parts[2]);
        var verifier = new Ed25519PublicKeyParameters(publicKeyBytes, 0);

        if (!verifier.Verify(
            Ed25519.Algorithm.Ed25519,
            null, // No RFC 8032 context: JWT signatures are plain Ed25519 over header.payload bytes.
            data,
            signature))
            throw new SecurityTokenInvalidSignatureException("Ed25519 signature verification failed.");

        return new JsonWebToken(token);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        switch (value.Length % 4)
        {
            case 2: value += "=="; break;
            case 3: value += "="; break;
        }
        return Convert.FromBase64String(value);
    }
}
