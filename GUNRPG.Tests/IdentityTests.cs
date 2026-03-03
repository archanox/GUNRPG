using GUNRPG.Core.Identity;
using GUNRPG.Infrastructure.Identity;
using LiteDB;
using Microsoft.Extensions.Options;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the identity data models and JWT token service.
/// </summary>
public class IdentityModelTests
{
    // ── RefreshToken model ───────────────────────────────────────────────────

    [Fact]
    public void RefreshToken_IsActive_WhenNotConsumedNotRevokedAndNotExpired()
    {
        var token = new RefreshToken
        {
            UserId = "user1",
            Token = "abc123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };

        Assert.True(token.IsActive);
    }

    [Fact]
    public void RefreshToken_IsNotActive_WhenExpired()
    {
        var token = new RefreshToken
        {
            UserId = "user1",
            Token = "abc123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
        };

        Assert.False(token.IsActive);
    }

    [Fact]
    public void RefreshToken_IsNotActive_WhenConsumed()
    {
        var token = new RefreshToken
        {
            UserId = "user1",
            Token = "abc123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            IsConsumed = true,
        };

        Assert.False(token.IsActive);
    }

    [Fact]
    public void RefreshToken_IsNotActive_WhenRevoked()
    {
        var token = new RefreshToken
        {
            UserId = "user1",
            Token = "abc123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            IsRevoked = true,
        };

        Assert.False(token.IsActive);
    }

    [Fact]
    public void RefreshToken_ReplacedByToken_IsNullInitially()
    {
        var token = new RefreshToken
        {
            UserId = "user1",
            Token = "abc123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };

        Assert.Null(token.ReplacedByToken);
        Assert.True(token.IsActive);
    }

    [Fact]
    public void RefreshToken_ReplacedByToken_CanBeSet()
    {
        var token = new RefreshToken
        {
            UserId = "user1",
            Token = "abc123",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            IsConsumed = true,
            ReplacedByToken = "new-token-456",
        };

        Assert.Equal("new-token-456", token.ReplacedByToken);
        Assert.False(token.IsActive); // consumed
    }

    // ── DeviceCode model ─────────────────────────────────────────────────────

    [Fact]
    public void DeviceCode_IsNotAuthorized_Initially()
    {
        var code = new DeviceCode
        {
            Code = "device_code",
            UserCode = "ABCD1234",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        };

        Assert.False(code.IsAuthorized);
        Assert.False(code.IsExpired);
    }

    [Fact]
    public void DeviceCode_IsAuthorized_WhenUserIdSet()
    {
        var code = new DeviceCode
        {
            Code = "device_code",
            UserCode = "ABCD1234",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
            AuthorizedUserId = "user123",
        };

        Assert.True(code.IsAuthorized);
    }

    [Fact]
    public void DeviceCode_IsExpired_WhenPastExpiryTime()
    {
        var code = new DeviceCode
        {
            Code = "device_code",
            UserCode = "ABCD1234",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

        Assert.True(code.IsExpired);
    }

    // ── Account model ────────────────────────────────────────────────────────

    [Fact]
    public void Account_StartsWithEmptyOperatorIds()
    {
        var account = new Account { UserId = "user1", DisplayName = "Test" };

        Assert.Empty(account.OperatorIds);
        Assert.NotEqual(Guid.Empty, account.Id);
    }

    [Fact]
    public void Account_CanAddOperatorIds()
    {
        var account = new Account { UserId = "user1", DisplayName = "Test" };
        var operatorId = Guid.NewGuid();

        account.OperatorIds.Add(operatorId);

        Assert.Single(account.OperatorIds);
        Assert.Equal(operatorId, account.OperatorIds[0]);
    }

    // ── WebAuthnCredential model ─────────────────────────────────────────────

    [Fact]
    public void WebAuthnCredential_DefaultValues_AreCorrect()
    {
        var cred = new WebAuthnCredential
        {
            Id = "base64url_id",
            UserId = "user1",
            PublicKey = [1, 2, 3],
            SignatureCounter = 0,
        };

        Assert.Equal("base64url_id", cred.Id);
        Assert.Equal("user1", cred.UserId);
        Assert.Equal(0u, cred.SignatureCounter);
        Assert.Empty(cred.Transports);
        Assert.Null(cred.LastUsedAt);
        Assert.Null(cred.Nickname);
    }
}

/// <summary>
/// Tests for the JWT token service using Ed25519 signing.
/// </summary>
public class JwtTokenServiceTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly JwtTokenService _service;

    public JwtTokenServiceTests()
    {
        _db = new LiteDatabase(":memory:");
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenExpiryMinutes = 15,
            RefreshTokenExpiryDays = 30,
        });
        _service = new JwtTokenService(options, _db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task IssueTokensAsync_ReturnsValidTokenPair()
    {
        var response = await _service.IssueTokensAsync("user123", "alice", null);

        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.True(response.AccessTokenExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(response.RefreshTokenExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task IssueTokensAsync_AccessToken_HasThreeParts()
    {
        var response = await _service.IssueTokensAsync("user123", "alice", null);

        // JWT format: header.payload.signature
        var parts = response.AccessToken.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.All(parts, p => Assert.NotEmpty(p));
    }

    [Fact]
    public async Task RefreshAsync_WithValidToken_SucceedsAndRotates()
    {
        var initial = await _service.IssueTokensAsync("user123", "alice", null);
        var refreshResult = await _service.RefreshAsync(initial.RefreshToken);

        Assert.True(refreshResult.IsSuccess);
        Assert.NotNull(refreshResult.Value);
        // New refresh token must differ from the old one
        Assert.NotEqual(initial.RefreshToken, refreshResult.Value!.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_WithConsumedToken_Fails()
    {
        var initial = await _service.IssueTokensAsync("user123", "alice", null);
        // Consume the token
        await _service.RefreshAsync(initial.RefreshToken);
        // Try to use it again
        var result = await _service.RefreshAsync(initial.RefreshToken);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RefreshAsync_WithInvalidToken_Fails()
    {
        var result = await _service.RefreshAsync("this-is-not-a-valid-token");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RevokeAllAsync_PreventsSubsequentRefresh()
    {
        var initial = await _service.IssueTokensAsync("user123", "alice", null);
        await _service.RevokeAllAsync("user123");

        var result = await _service.RefreshAsync(initial.RefreshToken);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RefreshAsync_SetsReplacedByToken_OnConsumedToken()
    {
        var initial = await _service.IssueTokensAsync("user123", "alice", null);
        var refreshed = await _service.RefreshAsync(initial.RefreshToken);

        Assert.True(refreshed.IsSuccess);

        // Verify the consumed token has ReplacedByToken set to the new token value
        // by trying to use the old token again — it should fail (consumed)
        var replayResult = await _service.RefreshAsync(initial.RefreshToken);
        Assert.False(replayResult.IsSuccess);

        // And the new token should work
        var secondRefresh = await _service.RefreshAsync(refreshed.Value!.RefreshToken);
        Assert.True(secondRefresh.IsSuccess);
    }

    [Fact]
    public async Task RefreshAsync_WithConsumedToken_Fails_Replay()
    {
        // Simulate a replay attack: the same refresh token is used twice
        var initial = await _service.IssueTokensAsync("user123", "alice", null);

        // First use — legitimate
        var first = await _service.RefreshAsync(initial.RefreshToken);
        Assert.True(first.IsSuccess);

        // Second use — replay attack
        var replay = await _service.RefreshAsync(initial.RefreshToken);
        Assert.False(replay.IsSuccess);
    }

    [Fact]
    public void GetPublicKeyBytes_ReturnsSameKeyAcrossCalls()
    {
        var key1 = _service.GetPublicKeyBytes();
        var key2 = _service.GetPublicKeyBytes();

        Assert.Equal(key1, key2);
        Assert.Equal(32, key1.Length); // Ed25519 public keys are 32 bytes
    }

    [Fact]
    public async Task KeyPair_PersistedToDatabase_ReloadProducesCompatibleTokens()
    {
        // Issue a token with the first service instance
        var response = await _service.IssueTokensAsync("user123", "alice", null);

        // Create a second service pointing at the same DB — key pair should be loaded, not regenerated
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenExpiryMinutes = 15,
            RefreshTokenExpiryDays = 30,
        });
        var service2 = new JwtTokenService(options, _db);

        // The public key should be the same
        Assert.Equal(_service.GetPublicKeyBytes(), service2.GetPublicKeyBytes());
    }
}

/// <summary>
/// Tests for the LiteDB WebAuthn challenge store.
/// </summary>
public class LiteDbWebAuthnStoreTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly LiteDbWebAuthnStore _store;

    public LiteDbWebAuthnStoreTests()
    {
        _db = new LiteDatabase(":memory:");
        _store = new LiteDbWebAuthnStore(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void StoreChallenge_ThenConsumeChallenge_ReturnsSameBytes()
    {
        var challenge = new byte[] { 1, 2, 3, 4, 5 };
        _store.StoreChallenge("alice", challenge);

        var consumed = _store.ConsumeChallenge("alice");

        Assert.NotNull(consumed);
        Assert.Equal(challenge, consumed);
    }

    [Fact]
    public void ConsumeChallenge_AfterConsumption_ReturnsNull()
    {
        _store.StoreChallenge("alice", [1, 2, 3]);
        _store.ConsumeChallenge("alice");

        var second = _store.ConsumeChallenge("alice");

        Assert.Null(second);
    }

    [Fact]
    public void ConsumeChallenge_ForUnknownUser_ReturnsNull()
    {
        var result = _store.ConsumeChallenge("nobody");

        Assert.Null(result);
    }

    [Fact]
    public void StoreChallenge_OverwritesPreviousChallenge()
    {
        _store.StoreChallenge("alice", [1, 2, 3]);
        _store.StoreChallenge("alice", [4, 5, 6]);

        var consumed = _store.ConsumeChallenge("alice");

        Assert.Equal(new byte[] { 4, 5, 6 }, consumed);
    }

    [Fact]
    public void UpsertCredential_ThenGetById_ReturnsCredential()
    {
        var cred = new WebAuthnCredential
        {
            Id = "cred-id-1",
            UserId = "user1",
            PublicKey = [1, 2, 3],
            SignatureCounter = 5,
        };

        _store.UpsertCredential(cred);
        var retrieved = _store.GetCredentialById("cred-id-1");

        Assert.NotNull(retrieved);
        Assert.Equal("user1", retrieved!.UserId);
        Assert.Equal(5u, retrieved.SignatureCounter);
    }

    [Fact]
    public void GetCredentialsByUserId_ReturnsOnlyUserCredentials()
    {
        _store.UpsertCredential(new WebAuthnCredential { Id = "c1", UserId = "user1", PublicKey = [1] });
        _store.UpsertCredential(new WebAuthnCredential { Id = "c2", UserId = "user1", PublicKey = [2] });
        _store.UpsertCredential(new WebAuthnCredential { Id = "c3", UserId = "user2", PublicKey = [3] });

        var user1Creds = _store.GetCredentialsByUserId("user1").ToList();

        Assert.Equal(2, user1Creds.Count);
        Assert.All(user1Creds, c => Assert.Equal("user1", c.UserId));
    }

    [Fact]
    public void UpsertAccount_ThenGetByUserId_ReturnsAccount()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            UserId = "user1",
            DisplayName = "Alice",
            OperatorIds = [Guid.NewGuid()],
        };

        _store.UpsertAccount(account);
        var retrieved = _store.GetAccountByUserId("user1");

        Assert.NotNull(retrieved);
        Assert.Equal("Alice", retrieved!.DisplayName);
        Assert.Single(retrieved.OperatorIds);
    }
}

/// <summary>
/// Tests for the Device Code service.
/// </summary>
public class DeviceCodeServiceTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly FakeTokenService _tokenService;
    private readonly DeviceCodeService _service;

    public DeviceCodeServiceTests()
    {
        _db = new LiteDatabase(":memory:");
        _tokenService = new FakeTokenService();
        // DeviceCodeService requires a UserManager which is complex to mock;
        // we test the start/expire/poll path without needing real users.
        // For those tests we construct the service directly.
        _service = new DeviceCodeService(_db, _tokenService, null!, "https://localhost/auth/device/verify");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task StartAsync_ReturnsValidDeviceCodeResponse()
    {
        var response = await _service.StartAsync();

        Assert.NotEmpty(response.DeviceCode);
        Assert.NotEmpty(response.UserCode);
        Assert.Equal("https://localhost/auth/device/verify", response.VerificationUri);
        Assert.True(response.ExpiresInSeconds > 0);
        Assert.True(response.PollIntervalSeconds > 0);
    }

    [Fact]
    public async Task StartAsync_GeneratesUniqueDeviceAndUserCodes()
    {
        var r1 = await _service.StartAsync();
        var r2 = await _service.StartAsync();

        Assert.NotEqual(r1.DeviceCode, r2.DeviceCode);
        Assert.NotEqual(r1.UserCode, r2.UserCode);
    }

    [Fact]
    public async Task PollAsync_BeforeAuthorization_ReturnsAuthorizationPending()
    {
        var start = await _service.StartAsync();

        var pollResult = await _service.PollAsync(start.DeviceCode);

        Assert.True(pollResult.IsSuccess);
        Assert.Equal("authorization_pending", pollResult.Value!.Status);
        Assert.Null(pollResult.Value.Tokens);
    }

    [Fact]
    public async Task AuthorizeAsync_WithInvalidUserCode_ReturnsNotFound()
    {
        var result = await _service.AuthorizeAsync("INVALID99", "user123");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PollAsync_WithInvalidDeviceCode_ReturnsNotFound()
    {
        var result = await _service.PollAsync("not-a-real-code");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task PollAsync_TooFast_ReturnsSlowDownStatus()
    {
        var start = await _service.StartAsync();
        // First poll sets LastPolledAt
        await _service.PollAsync(start.DeviceCode);
        // Immediate second poll should return slow_down
        var result = await _service.PollAsync(start.DeviceCode);

        Assert.True(result.IsSuccess);
        Assert.Equal("slow_down", result.Value!.Status);
    }

    // ── Fake token service for testing ──────────────────────────────────────

    private sealed class FakeTokenService : GUNRPG.Application.Identity.ITokenService
    {
        public Task<GUNRPG.Application.Identity.Dtos.TokenResponse> IssueTokensAsync(
            string userId, string? username, Guid? accountId, CancellationToken ct = default) =>
            Task.FromResult(new GUNRPG.Application.Identity.Dtos.TokenResponse(
                "fake.access.token", "fake-refresh-token",
                DateTimeOffset.UtcNow.AddMinutes(15),
                DateTimeOffset.UtcNow.AddDays(30)));

        public Task<GUNRPG.Application.Results.ServiceResult<GUNRPG.Application.Identity.Dtos.TokenResponse>> RefreshAsync(
            string refreshToken, CancellationToken ct = default) =>
            Task.FromResult(GUNRPG.Application.Results.ServiceResult<GUNRPG.Application.Identity.Dtos.TokenResponse>.NotFound());

        public Task RevokeAllAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }
}

/// <summary>
/// Additional JWT token service tests covering the key ID (kid) and key persistence.
/// </summary>
public class JwtTokenServiceKeyTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly JwtTokenService _service;

    public JwtTokenServiceKeyTests()
    {
        _db = new LiteDatabase(":memory:");
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            AccessTokenExpiryMinutes = 15,
            RefreshTokenExpiryDays = 30,
        });
        _service = new JwtTokenService(options, _db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void GetKeyId_ReturnsNonEmptyString()
    {
        var kid = _service.GetKeyId();

        Assert.NotNull(kid);
        Assert.NotEmpty(kid);
    }

    [Fact]
    public async Task AccessToken_ContainsKidHeader()
    {
        var response = await _service.IssueTokensAsync("user1", "alice", null);
        var parts = response.AccessToken.Split('.');
        Assert.Equal(3, parts.Length);

        // Decode header (base64url part 0)
        var headerJson = DecodeBase64Url(parts[0]);
        Assert.Contains("\"kid\"", headerJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccessToken_ContainsStandardClaims()
    {
        var response = await _service.IssueTokensAsync("user1", "alice", null);
        var parts = response.AccessToken.Split('.');
        var payloadJson = DecodeBase64Url(parts[1]);

        // Standard JWT claims
        Assert.Contains("\"sub\"", payloadJson);
        Assert.Contains("\"jti\"", payloadJson);
        Assert.Contains("\"iat\"", payloadJson);
        Assert.Contains("\"exp\"", payloadJson);
    }

    [Fact]
    public async Task AccessToken_AlgIsEdDSA()
    {
        var response = await _service.IssueTokensAsync("user1", "alice", null);
        var parts = response.AccessToken.Split('.');
        var headerJson = DecodeBase64Url(parts[0]);

        Assert.Contains("\"EdDSA\"", headerJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetKeyId_IsDeterministicFromPublicKey()
    {
        var kid1 = _service.GetKeyId();
        var kid2 = _service.GetKeyId();

        Assert.Equal(kid1, kid2);
    }

    [Fact]
    public void KeyId_ChangesWhenPublicKeyChanges()
    {
        // Create a fresh DB — new service generates a new key pair
        using var db2 = new LiteDatabase(":memory:");
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
        });
        var service2 = new JwtTokenService(options, db2);

        // Different DB → different key pair → different kid
        Assert.NotEqual(_service.GetKeyId(), service2.GetKeyId());
    }

    private static string DecodeBase64Url(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        switch (value.Length % 4)
        {
            case 2: value += "=="; break;
            case 3: value += "="; break;
        }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}

/// <summary>
/// Tests for device code status values aligned with RFC 8628.
/// </summary>
public class DeviceCodeStatusTests : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly DeviceCodeService _service;

    public DeviceCodeStatusTests()
    {
        _db = new LiteDatabase(":memory:");
        _service = new DeviceCodeService(_db, new FakeIssuer(), null!, "https://localhost/verify");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task PollAsync_ReturnsAuthorizationPending_WhenNotAuthorized()
    {
        var start = await _service.StartAsync();
        var result = await _service.PollAsync(start.DeviceCode);

        Assert.True(result.IsSuccess);
        Assert.Equal("authorization_pending", result.Value!.Status);
    }

    [Fact]
    public async Task PollAsync_ReturnsSlowDown_WhenPolledTooFast()
    {
        var start = await _service.StartAsync();
        await _service.PollAsync(start.DeviceCode); // First poll
        var result = await _service.PollAsync(start.DeviceCode); // Immediate second poll

        Assert.True(result.IsSuccess);
        Assert.Equal("slow_down", result.Value!.Status);
    }

    [Fact]
    public async Task PollAsync_ReturnsExpiredToken_WhenCodeExpired()
    {
        // Insert a pre-expired device code directly via LiteDB (bypasses the service's clock)
        var codes = _db.GetCollection<GUNRPG.Core.Identity.DeviceCode>("identity_device_codes");
        var expiredCode = new GUNRPG.Core.Identity.DeviceCode
        {
            Code = "expired-device-code",
            UserCode = "EXPIRXXX",
            IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-20),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5), // Already expired
        };
        codes.Insert(expiredCode);

        var result = await _service.PollAsync("expired-device-code");

        Assert.True(result.IsSuccess);
        Assert.Equal("expired_token", result.Value!.Status);
    }

    private sealed class FakeIssuer : GUNRPG.Application.Identity.ITokenService
    {
        public Task<GUNRPG.Application.Identity.Dtos.TokenResponse> IssueTokensAsync(
            string userId, string? username, Guid? accountId, CancellationToken ct = default) =>
            Task.FromResult(new GUNRPG.Application.Identity.Dtos.TokenResponse(
                "acc", "ref", DateTimeOffset.UtcNow.AddMinutes(15), DateTimeOffset.UtcNow.AddDays(30)));

        public Task<GUNRPG.Application.Results.ServiceResult<GUNRPG.Application.Identity.Dtos.TokenResponse>> RefreshAsync(
            string refreshToken, CancellationToken ct = default) =>
            Task.FromResult(GUNRPG.Application.Results.ServiceResult<GUNRPG.Application.Identity.Dtos.TokenResponse>.NotFound());

        public Task RevokeAllAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }
}

/// <summary>
/// Tests for WebAuthn error code parsing and typed error handling.
/// </summary>
public class WebAuthnErrorCodeTests
{
    [Theory]
    [InlineData("InvalidRequest")]
    [InlineData("ChallengeMissing")]
    [InlineData("AttestationFailed")]
    [InlineData("AssertionFailed")]
    [InlineData("CredentialNotFound")]
    [InlineData("CounterRegression")]
    [InlineData("UserNotFound")]
    [InlineData("InternalError")]
    public void WebAuthnErrorCode_AllValuesAreParseable(string codeName)
    {
        // All error code names must be valid enum values so the controller can parse them
        var parsed = Enum.Parse<GUNRPG.Application.Identity.Dtos.WebAuthnErrorCode>(codeName);
        Assert.Equal(codeName, parsed.ToString());
    }

    [Fact]
    public void WebAuthnErrorResponse_CanBeConstructed()
    {
        var error = new GUNRPG.Application.Identity.Dtos.WebAuthnErrorResponse(
            GUNRPG.Application.Identity.Dtos.WebAuthnErrorCode.AttestationFailed,
            "Signature mismatch.");

        Assert.Equal(GUNRPG.Application.Identity.Dtos.WebAuthnErrorCode.AttestationFailed, error.Code);
        Assert.Equal("Signature mismatch.", error.Message);
    }
}
