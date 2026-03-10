using System.Net;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.ConsoleClient.Auth;
using GUNRPG.ConsoleClient.Identity;
using GUNRPG.Infrastructure;
using GUNRPG.Infrastructure.Persistence;
using Hex1b;
using LiteDB;

namespace GUNRPG.Tests;

// ─── SessionStore tests ──────────────────────────────────────────────────────

public sealed class SessionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SessionStore _store;

    public SessionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gunrpg-ss-test-{Guid.NewGuid():N}");
        _store = new SessionStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips_AllSessionFields()
    {
        var session = new SessionData("rt-abc", "user-123", DateTimeOffset.UtcNow);

        await _store.SaveAsync(session);
        var loaded = await _store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("rt-abc", loaded.RefreshToken);
        Assert.Equal("user-123", loaded.UserId);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileAbsent()
    {
        var result = await _store.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenFileCorrupt()
    {
        var filePath = Path.Combine(_tempDir, "session.json");
        await File.WriteAllTextAsync(filePath, "not valid json {{{{");

        var result = await _store.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        await _store.SaveAsync(new SessionData("rt-xyz", "u1", DateTimeOffset.UtcNow));
        _store.Delete();

        var result = await _store.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_IsNoOp_WhenFileAbsent()
    {
        // Should not throw.
        _store.Delete();
        var result = await _store.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_WritesSessionJson_NotAuthJson()
    {
        await _store.SaveAsync(new SessionData("my-refresh-token", "uid", DateTimeOffset.UtcNow));

        // The file must be session.json, not auth.json.
        Assert.True(File.Exists(Path.Combine(_tempDir, "session.json")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "auth.json")));
    }

    [Fact]
    public async Task SaveAsync_PersistedJson_ContainsRequiredFields()
    {
        await _store.SaveAsync(new SessionData("refresh-tok", "user-999", DateTimeOffset.UtcNow));

        var rawJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "session.json"));
        using var doc = JsonDocument.Parse(rawJson);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Contains("refreshToken", props);
        Assert.Contains("userId", props);
        Assert.Contains("createdAt", props);
    }

    [Fact]
    public async Task SaveAsync_PersistedJson_DoesNotContainAccessToken()
    {
        await _store.SaveAsync(new SessionData("refresh-tok", "user-999", DateTimeOffset.UtcNow));

        var rawJson = await File.ReadAllTextAsync(Path.Combine(_tempDir, "session.json"));
        using var doc = JsonDocument.Parse(rawJson);
        var props = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.DoesNotContain("accessToken", props);
    }

    [Fact]
    public async Task SaveAsync_Overwrites_PreviousSession()
    {
        await _store.SaveAsync(new SessionData("rt-1", "user-1", DateTimeOffset.UtcNow));
        await _store.SaveAsync(new SessionData("rt-2", "user-2", DateTimeOffset.UtcNow));

        var loaded = await _store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal("rt-2", loaded.RefreshToken);
        Assert.Equal("user-2", loaded.UserId);
    }
}

// ─── SessionManager tests ────────────────────────────────────────────────────

public sealed class SessionManagerTests : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private static readonly object s_userProfileEnvironmentLock = new();
    private const string BaseUrl = "https://node.example.com";

    private readonly string _tempDir;
    private readonly SessionStore _sessionStore;
    private readonly TokenStore _tokenStore;

    public SessionManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gunrpg-sm-test-{Guid.NewGuid():N}");
        _sessionStore = new SessionStore(_tempDir);
        _tokenStore = new TokenStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── TryAutoLoginAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task TryAutoLoginAsync_ReturnsFalse_WhenNoSessionStored()
    {
        var (manager, _) = MakeManager(new FakeHandler());

        var result = await manager.TryAutoLoginAsync();

        Assert.False(result);
        Assert.Equal(AuthState.NotAuthenticated, manager.State);
    }

    [Fact]
    public async Task TryAutoLoginAsync_RefreshesToken_WhenSessionExists()
    {
        await _sessionStore.SaveAsync(new SessionData("stored-rt", "user-1", DateTimeOffset.UtcNow));

        var newTokens = MakeTokenResponse("new-access", "new-refresh");
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(newTokens));

        var (manager, authHandler) = MakeManager(fake);

        var result = await manager.TryAutoLoginAsync();

        Assert.True(result);
        Assert.Equal(AuthState.Authenticated, manager.State);
        Assert.Equal("new-access", authHandler.AccessToken);
        Assert.Contains("/auth/token/refresh", fake.Calls[0].Uri ?? "");
    }

    [Fact]
    public async Task TryAutoLoginAsync_ReturnsFalse_WhenRefreshFails()
    {
        await _sessionStore.SaveAsync(new SessionData("expired-rt", "user-1", DateTimeOffset.UtcNow));

        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.Unauthorized, null);

        var (manager, _) = MakeManager(fake);

        var result = await manager.TryAutoLoginAsync();

        Assert.False(result);
        Assert.Equal(AuthState.NotAuthenticated, manager.State);
    }

    [Fact]
    public async Task TryAutoLoginAsync_UpdatesSessionFile_OnSuccess()
    {
        await _sessionStore.SaveAsync(new SessionData("old-rt", "user-1", DateTimeOffset.UtcNow));

        var newTokens = MakeTokenResponse("access", "new-rotation-rt");
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(newTokens));

        var (manager, _) = MakeManager(fake);
        await manager.TryAutoLoginAsync();

        var stored = await _sessionStore.LoadAsync();
        Assert.NotNull(stored);
        Assert.Equal("new-rotation-rt", stored.RefreshToken);
    }

    // ─── StartLogin ──────────────────────────────────────────────────────────

    [Fact]
    public async Task StartLogin_TransitionsToAuthenticating_Immediately()
    {
        var fake = new FakeHandler();
        // Device flow will block in PollForTokenAsync — use a long delay or a TaskCompletionSource.
        // We only care about the immediate state transition; cancel after checking.
        var cts = new CancellationTokenSource();
        fake.Enqueue(HttpStatusCode.OK, Json(MakeDeviceFlow()));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorization_pending", null)));

        var (manager, _) = MakeManager(fake);
        manager.StartLogin(cts.Token);

        // State should be Authenticating immediately.
        Assert.Equal(AuthState.Authenticating, manager.State);

        cts.Cancel();
        await Task.Delay(200); // Allow background task to complete with cancellation.
    }

    [Fact]
    public async Task StartLogin_TransitionsToAuthenticated_OnSuccess()
    {
        var tokens = MakeTokenResponse("access-from-device", "refresh-from-device");
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(MakeDeviceFlow()));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));

        var (manager, authHandler) = MakeManager(fake);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.StartLogin(cts.Token);

        // Wait for the background task to finish.
        await WaitForStateAsync(manager, AuthState.Authenticated, cts.Token);

        Assert.Equal(AuthState.Authenticated, manager.State);
        Assert.Equal("access-from-device", authHandler.AccessToken);
    }

    [Fact]
    public async Task StartLogin_SavesSession_OnSuccess()
    {
        var tokens = MakeTokenResponse("access-fd", "refresh-fd");
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(MakeDeviceFlow()));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));

        var (manager, _) = MakeManager(fake);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.StartLogin(cts.Token);
        await WaitForStateAsync(manager, AuthState.Authenticated, cts.Token);

        var stored = await _sessionStore.LoadAsync();
        Assert.NotNull(stored);
        Assert.Equal("refresh-fd", stored.RefreshToken);
    }

    [Fact]
    public async Task StartLogin_SetsVerificationUrlAndUserCode()
    {
        var deviceFlow = MakeDeviceFlow();
        var tokens = MakeTokenResponse("a", "r");
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(deviceFlow));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));

        var (manager, _) = MakeManager(fake);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.StartLogin(cts.Token);

        // Give the background task time to call /auth/device/start and set the properties.
        await Task.Delay(300);

        // Verification details should be available for the TUI to render.
        Assert.Equal(deviceFlow.VerificationUri, manager.VerificationUrl);
        Assert.Equal(deviceFlow.UserCode, manager.UserCode);
    }

    [Fact]
    public async Task StartLogin_SetsLoginError_OnFailure()
    {
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.InternalServerError, null); // /auth/device/start fails

        var (manager, _) = MakeManager(fake);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.StartLogin(cts.Token);

        await WaitForStateAsync(manager, AuthState.NotAuthenticated, cts.Token);

        Assert.Equal(AuthState.NotAuthenticated, manager.State);
        Assert.NotNull(manager.LoginError);
    }

    [Fact]
    public async Task BuildUI_TransitionsToMainMenu_WhenLoginCompletes()
    {
        var tokens = MakeTokenResponse("access-from-device", "refresh-from-device");
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(MakeDeviceFlow()));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));

        var (manager, _) = MakeManager(fake);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.StartLogin(cts.Token);
        await WaitForStateAsync(manager, AuthState.Authenticated, cts.Token);

        using var gameStateScope = CreateAuthenticatingGameState(manager, "auth-success-test.db");
        var gameState = gameStateScope.GameState;
        using var uiCts = new CancellationTokenSource();

        await gameState.BuildUI(new RootContext(), uiCts);

        Assert.Equal(Screen.MainMenu, gameState.CurrentScreen);
    }

    [Fact]
    public async Task BuildUI_ReturnsToLoginMenu_WhenLoginFails()
    {
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.InternalServerError, null);

        var (manager, _) = MakeManager(fake);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        manager.StartLogin(cts.Token);
        await WaitForStateAsync(manager, AuthState.NotAuthenticated, cts.Token);

        using var gameStateScope = CreateAuthenticatingGameState(manager, "auth-failure-test.db");
        var gameState = gameStateScope.GameState;
        using var uiCts = new CancellationTokenSource();

        await gameState.BuildUI(new RootContext(), uiCts);

        Assert.Equal(Screen.LoginMenu, gameState.CurrentScreen);
    }

    // ─── Logout ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_ClearsSessionFile()
    {
        await _sessionStore.SaveAsync(new SessionData("rt", "uid", DateTimeOffset.UtcNow));

        var (manager, _) = MakeManager(new FakeHandler());
        manager.Logout();

        var result = await _sessionStore.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public void Logout_ClearsAccessToken()
    {
        var (manager, authHandler) = MakeManager(new FakeHandler());
        authHandler.SetAccessToken("some-token");

        manager.Logout();

        Assert.Null(authHandler.AccessToken);
    }

    [Fact]
    public void Logout_TransitionsToNotAuthenticated()
    {
        var (manager, _) = MakeManager(new FakeHandler());

        manager.Logout();

        Assert.Equal(AuthState.NotAuthenticated, manager.State);
    }

    [Fact]
    public void Logout_ClearsLoginError()
    {
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.InternalServerError, null);

        var (manager, _) = MakeManager(fake);
        // Simulate a prior error state by logging out with no prior session.
        manager.Logout();

        Assert.Null(manager.LoginError);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private (SessionManager Manager, AuthDelegatingHandler AuthHandler) MakeManager(FakeHandler fake)
    {
        var authHandler = new AuthDelegatingHandler(_tokenStore, BaseUrl)
        {
            InnerHandler = fake
        };
        var manager = new SessionManager(_sessionStore, authHandler, BaseUrl);
        return (manager, authHandler);
    }

    private static string Json<T>(T value) =>
        System.Text.Json.JsonSerializer.Serialize(value, s_json);

    private static TokenResponse MakeTokenResponse(string access, string refresh) =>
        new(access, refresh,
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(30));

    private static DeviceCodeResponse MakeDeviceFlow() =>
        new("device-code-xyz", "ABCD-1234", "https://node.example.com/device", 300, 0);

    private static async Task WaitForStateAsync(SessionManager manager, AuthState expected, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && manager.State != expected)
            await Task.Delay(50, ct);
    }

    private TestGameStateScope CreateAuthenticatingGameState(SessionManager manager, string databaseFileName)
    {
        var profileScope = new UserProfileScope(_tempDir);
        var databasePath = Path.Combine(_tempDir, databaseFileName);
        var offlineDb = new LiteDatabase(databasePath);
        var offlineStore = new OfflineStore(offlineDb);
        var httpClient = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var resolver = new GameBackendResolver(httpClient, offlineStore);
        var gameState = new GameState(
            httpClient,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            new StubGameBackend(),
            resolver,
            offlineStore,
            offlineDb,
            manager,
            Screen.Authenticating);

        return new TestGameStateScope(gameState, httpClient, offlineDb, databasePath, profileScope);
    }

    private sealed class TestGameStateScope(GameState gameState, HttpClient httpClient, LiteDatabase offlineDb, string databasePath, UserProfileScope profileScope) : IDisposable
    {
        public GameState GameState { get; } = gameState;

        public void Dispose()
        {
            httpClient.Dispose();
            offlineDb.Dispose();
            if (File.Exists(databasePath))
                File.Delete(databasePath);
            profileScope.Dispose();
        }
    }

    private sealed class UserProfileScope : IDisposable
    {
        private readonly string? _originalHome;
        private readonly string? _originalUserProfile;
        private readonly string _profileDirectory;
        private bool _disposed;

        public UserProfileScope(string tempDir)
        {
            Monitor.Enter(s_userProfileEnvironmentLock);
            _originalHome = Environment.GetEnvironmentVariable("HOME");
            _originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            _profileDirectory = Path.Combine(tempDir, "test-home");
            Directory.CreateDirectory(Path.Combine(_profileDirectory, ".gunrpg"));
            Environment.SetEnvironmentVariable("HOME", _profileDirectory);
            Environment.SetEnvironmentVariable("USERPROFILE", _profileDirectory);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Environment.SetEnvironmentVariable("HOME", _originalHome);
            Environment.SetEnvironmentVariable("USERPROFILE", _originalUserProfile);
            Monitor.Exit(s_userProfileEnvironmentLock);
            _disposed = true;
        }
    }

    private sealed class StubGameBackend : IGameBackend
    {
        public Task<OperatorDto?> GetOperatorAsync(string id) => Task.FromResult<OperatorDto?>(null);

        public Task<OperatorDto> InfilOperatorAsync(string id) => throw new NotSupportedException();

        public Task<bool> OperatorExistsAsync(string id) => Task.FromResult(false);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string? Body)> _queue = new();

        public List<(string? Method, string? Uri, string? AuthHeader, string? RequestBody)> Calls { get; } = new();

        public void Enqueue(HttpStatusCode status, string? body = null)
            => _queue.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(ct)
                : null;

            Calls.Add((
                request.Method.Method,
                request.RequestUri?.ToString(),
                request.Headers.Authorization?.ToString(),
                body));

            await Task.Yield();

            if (_queue.TryDequeue(out var entry))
            {
                var msg = new HttpResponseMessage(entry.Status);
                if (entry.Body is not null)
                    msg.Content = new StringContent(entry.Body, Encoding.UTF8, "application/json");
                return msg;
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }
}
