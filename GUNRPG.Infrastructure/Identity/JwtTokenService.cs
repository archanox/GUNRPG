using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;
using GUNRPG.Core.Identity;
using LiteDB;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// JWT token service using Ed25519 (EdDSA) signing via BouncyCastle.
/// Ed25519 is a modern, compact, and high-performance signing algorithm.
/// The key pair is generated on first run and persisted to the LiteDB metadata collection.
///
/// Each issued token includes a <c>kid</c> (key ID) header — a SHA-256 thumbprint of the
/// Ed25519 public key bytes — enabling future key rotation: validators can select the correct
/// public key by looking up the <c>kid</c> in a published JWKS-like endpoint.
/// </summary>
public sealed class JwtTokenService : ITokenService
{
    private const string MetaCollection = "identity_meta";
    private const string PrivateKeyField = "ed25519_private_key";
    private const string PublicKeyField = "ed25519_public_key";

    private readonly JwtOptions _options;
    private readonly ILiteCollection<BsonDocument> _meta;
    private readonly ILiteCollection<RefreshToken> _refreshTokens;

    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;
    /// <summary>SHA-256 thumbprint of the public key, used as the JWT <c>kid</c> header.</summary>
    private readonly string _keyId;

    public JwtTokenService(IOptions<JwtOptions> options, ILiteDatabase db)
    {
        _options = options.Value;
        _meta = db.GetCollection<BsonDocument>(MetaCollection);
        _refreshTokens = db.GetCollection<RefreshToken>("identity_refresh_tokens");
        _refreshTokens.EnsureIndex(t => t.UserId);
        _refreshTokens.EnsureIndex(t => t.Token, unique: true);

        (_privateKey, _publicKey) = LoadOrGenerateKeyPair();
        _keyId = ComputeKeyId(_publicKey.GetEncoded());
    }

    // ── ITokenService ────────────────────────────────────────────────────────

    public async Task<TokenResponse> IssueTokensAsync(
        string userId,
        string? username,
        Guid? accountId,
        CancellationToken ct = default)
    {
        var accessToken = BuildAccessToken(userId, username, accountId);
        var refresh = await CreateRefreshTokenAsync(userId, ct);
        return new TokenResponse(
            accessToken,
            refresh.Token,
            DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenExpiryMinutes),
            refresh.ExpiresAt);
    }

    public async Task<ServiceResult<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var existing = _refreshTokens.FindOne(t => t.Token == refreshToken);
        if (existing is null || !existing.IsActive)
            return ServiceResult<TokenResponse>.InvalidState("Refresh token is invalid, expired, or already consumed.");

        // Rotate: consume old, issue new
        existing.IsConsumed = true;
        _refreshTokens.Update(existing);

        var newRefresh = await CreateRefreshTokenAsync(existing.UserId, ct);
        // On refresh we re-issue with stored userId only; caller should re-establish claims if needed
        var accessToken = BuildAccessToken(existing.UserId, username: null, accountId: null);

        return ServiceResult<TokenResponse>.Success(new TokenResponse(
            accessToken,
            newRefresh.Token,
            DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenExpiryMinutes),
            newRefresh.ExpiresAt));
    }

    public Task RevokeAllAsync(string userId, CancellationToken ct = default)
    {
        var tokens = _refreshTokens.Find(t => t.UserId == userId && !t.IsConsumed && !t.IsRevoked).ToList();
        foreach (var t in tokens)
        {
            t.IsRevoked = true;
            _refreshTokens.Update(t);
        }
        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string BuildAccessToken(string userId, string? username, Guid? accountId)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        if (username is not null)
            claims.Add(new(JwtRegisteredClaimNames.PreferredUsername, username));

        if (accountId.HasValue)
            claims.Add(new("account_id", accountId.Value.ToString()));

        var payload = BuildPayload(claims, now, now.AddMinutes(_options.AccessTokenExpiryMinutes));
        return SignToken(payload);
    }

    private string BuildPayload(IEnumerable<Claim> claims, DateTime notBefore, DateTime expires)
    {
        // Build header manually for EdDSA (alg=EdDSA) + key ID for future key rotation.
        var header = new JwtHeader
        {
            { JwtHeaderParameterNames.Alg, "EdDSA" },
            { JwtHeaderParameterNames.Typ, "JWT" },
            { JwtHeaderParameterNames.Kid, _keyId },
        };

        var payload = new JwtPayload(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires);

        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(header.SerializeToJson()))}.{Base64UrlEncode(Encoding.UTF8.GetBytes(payload.SerializeToJson()))}";
    }

    private string SignToken(string headerDotPayload)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        var data = Encoding.UTF8.GetBytes(headerDotPayload);
        signer.BlockUpdate(data, 0, data.Length);
        var signature = signer.GenerateSignature();
        return $"{headerDotPayload}.{Base64UrlEncode(signature)}";
    }

    private Task<RefreshToken> CreateRefreshTokenAsync(string userId, CancellationToken _)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = new RefreshToken
        {
            UserId = userId,
            Token = Base64UrlEncode(tokenBytes),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenExpiryDays),
        };
        _refreshTokens.Insert(token);
        return Task.FromResult(token);
    }

    private (Ed25519PrivateKeyParameters, Ed25519PublicKeyParameters) LoadOrGenerateKeyPair()
    {
        var existingDoc = _meta.FindOne(d => d["_id"] == PrivateKeyField);
        if (existingDoc is not null)
        {
            var privateBytes = existingDoc[PrivateKeyField].AsBinary;
            var publicBytes = existingDoc[PublicKeyField].AsBinary;
            return (new Ed25519PrivateKeyParameters(privateBytes), new Ed25519PublicKeyParameters(publicBytes));
        }

        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var priv = (Ed25519PrivateKeyParameters)pair.Private;
        var pub = (Ed25519PublicKeyParameters)pair.Public;

        _meta.Upsert(new BsonDocument
        {
            ["_id"] = PrivateKeyField,
            [PrivateKeyField] = priv.GetEncoded(),
            [PublicKeyField] = pub.GetEncoded(),
        });

        return (priv, pub);
    }

    /// <summary>Exposes the Ed25519 public key bytes for node-to-node trust exchange.</summary>
    public byte[] GetPublicKeyBytes() => _publicKey.GetEncoded();

    /// <summary>
    /// Returns the JWT <c>kid</c> (key ID) — a SHA-256 thumbprint of the public key bytes.
    /// Future validators can use this to select the correct public key from a JWKS-like endpoint
    /// when key rotation is implemented.
    /// </summary>
    public string GetKeyId() => _keyId;

    private static string ComputeKeyId(byte[] publicKeyBytes)
    {
        var hash = SHA256.HashData(publicKeyBytes);
        return Base64UrlEncode(hash[..16]); // 16 bytes = 128-bit ID, compact but unique
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
