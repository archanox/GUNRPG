using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// JWT token management endpoints: refresh token rotation and public key exposure.
/// </summary>
[ApiController]
[Route("auth/token")]
public sealed class TokenController : ControllerBase
{
    private readonly ITokenService _tokens;
    private readonly IPublicKeyProvider _publicKey;

    public TokenController(ITokenService tokens, IPublicKeyProvider publicKey)
    {
        _tokens = tokens;
        _publicKey = publicKey;
    }

    /// <summary>
    /// Exchanges a valid refresh token for a new access + refresh token pair (rotation).
    /// The old refresh token is consumed on exchange and cannot be reused.
    /// Replay attempts (double-use of a consumed token) return 401 Unauthorized.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { error = "refresh_token is required." });

        var result = await _tokens.RefreshAsync(request.RefreshToken, ct);

        if (!result.IsSuccess)
        {
            // Distinguish consumed/revoked tokens (replay attempts) from truly invalid tokens.
            // Both map to 401 to avoid leaking internal state — client must re-authenticate.
            return Unauthorized(new
            {
                error = "invalid_grant",
                error_description = "The refresh token is invalid, expired, revoked, or has already been used.",
            });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Returns the node's Ed25519 public key as a JWK-like object.
    /// Intended for future node-to-node trust exchange: remote nodes can fetch this key
    /// to verify JWTs issued by this server without a centralised IdP.
    /// </summary>
    [HttpGet("public-key")]
    public IActionResult GetPublicKey()
    {
        var keyBytes = _publicKey.GetPublicKeyBytes();
        var kid = _publicKey.GetKeyId();

        return Ok(new
        {
            alg = "EdDSA",
            crv = "Ed25519",
            kty = "OKP",
            kid,
            x = Base64UrlEncode(keyBytes),
        });
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
