using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;
using GUNRPG.Core.Identity;
using GUNRPG.Security;
using LiteDB;
using Microsoft.Extensions.Options;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// JWT token service using Ed25519 (EdDSA) signing.
/// Ed25519 is a modern, compact, and high-performance signing algorithm.
/// The key pair is generated on first run and persisted to the LiteDB metadata collection.
///
/// Each issued token includes a <c>kid</c> (key ID) header — a SHA-256 thumbprint of the
/// Ed25519 public key bytes — enabling future key rotation: validators can select the correct
/// public key by looking up the <c>kid</c> in a published JWKS-like endpoint.
/// </summary>
public sealed class JwtTokenService : ITokenService, IPublicKeyProvider
{
    private const string MetaCollection = "identity_meta";
    private const string PrivateKeyField = "ed25519_private_key";
    private const string PublicKeyField = "ed25519_public_key";

    private readonly JwtOptions _options;
    private readonly ILiteCollection<BsonDocument> _meta;
    private readonly ILiteCollection<RefreshToken> _refreshTokens;
    private readonly ILiteCollection<ApplicationUser> _users;

    private readonly byte[] _privateKey;
    private readonly byte[] _publicKey;
    /// <summary>SHA-256 thumbprint of the public key, used as the JWT <c>kid</c> header.</summary>
    private readonly string _keyId;

    public JwtTokenService(IOptions<JwtOptions> options, ILiteDatabase db)
    {
        _options = options.Value;
        _meta = db.GetCollection<BsonDocument>(MetaCollection);
        _refreshTokens = db.GetCollection<RefreshToken>("identity_refresh_tokens");
        _users = db.GetCollection<ApplicationUser>(LiteDbUserStore.CollectionName);
        _refreshTokens.EnsureIndex(t => t.UserId);
        _refreshTokens.EnsureIndex(t => t.Token, unique: true);

        (_privateKey, _publicKey) = LoadOrGenerateKeyPair();
        _keyId = ComputeKeyId(_publicKey);
    }

    // ── ITokenService ────────────────────────────────────────────────────────

    public async Task<TokenResponse> IssueTokensAsync(
        string userId,
        string? username,
        Guid? accountId,
        CancellationToken ct = default)
    {
        var accessToken = BuildAccessToken(userId, username, accountId);
        var refresh = await CreateRefreshTokenAsync(userId, username, accountId, ct);
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

        var accountId = existing.AccountId;
        if (!accountId.HasValue || accountId.Value == Guid.Empty)
        {
            var accountProvisioning = await AccountIdProvisioning.EnsureAssignedAsync(_users, existing.UserId, ct);
            if (!accountProvisioning.Result.Succeeded)
            {
                return ServiceResult<TokenResponse>.InvalidState(
                    string.Join("; ", accountProvisioning.Result.Errors.Select(e => e.Description)));
            }

            accountId = accountProvisioning.AccountId;
        }

        // Rotate: consume old, issue new (preserving original username/accountId claims)
        existing.IsConsumed = true;
        existing.AccountId = accountId;
        var newRefresh = await CreateRefreshTokenAsync(existing.UserId, existing.Username, accountId, ct);
        existing.ReplacedByToken = newRefresh.Token;
        _refreshTokens.Update(existing);

        var accessToken = BuildAccessToken(existing.UserId, existing.Username, accountId);

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
        var data = Encoding.UTF8.GetBytes(headerDotPayload);
        var signature = AuthorityCrypto.SignPayload(_privateKey, data);
        return $"{headerDotPayload}.{Base64UrlEncode(signature)}";
    }

    private Task<RefreshToken> CreateRefreshTokenAsync(string userId, string? username, Guid? accountId, CancellationToken _)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = new RefreshToken
        {
            UserId = userId,
            Token = Base64UrlEncode(tokenBytes),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenExpiryDays),
            Username = username,
            AccountId = accountId,
        };
        _refreshTokens.Insert(token);
        return Task.FromResult(token);
    }

    private (byte[] PrivateKey, byte[] PublicKey) LoadOrGenerateKeyPair()
    {
        var existingDoc = _meta.FindOne(d => d["_id"] == PrivateKeyField);
        if (existingDoc is not null)
        {
            var privateBytes = AuthorityCrypto.CloneAndValidatePrivateKey(existingDoc[PrivateKeyField].AsBinary);
            var publicBytes = AuthorityCrypto.CloneAndValidatePublicKey(existingDoc[PublicKeyField].AsBinary);
            var derivedPublicBytes = AuthorityCrypto.GetPublicKey(privateBytes);

            if (!CryptographicOperations.FixedTimeEquals(publicBytes, derivedPublicBytes))
            {
                existingDoc[PublicKeyField] = derivedPublicBytes;
                _meta.Upsert(existingDoc);
                return (privateBytes, derivedPublicBytes);
            }

            return (privateBytes, publicBytes);
        }

        var priv = AuthorityCrypto.GeneratePrivateKey();
        var pub = AuthorityCrypto.GetPublicKey(priv);

        _meta.Upsert(new BsonDocument
        {
            ["_id"] = PrivateKeyField,
            [PrivateKeyField] = priv,
            [PublicKeyField] = pub,
        });

        return (priv, pub);
    }

    /// <summary>Exposes the Ed25519 public key bytes for node-to-node trust exchange.</summary>
    public byte[] GetPublicKeyBytes() => (byte[])_publicKey.Clone();

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
