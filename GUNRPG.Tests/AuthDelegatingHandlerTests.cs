using System.Net;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.ConsoleClient.Identity;

namespace GUNRPG.Tests;

/// <summary>
/// Unit tests for <see cref="AuthDelegatingHandler"/>.
///
/// All requests (both game-play and auth bypass) go through the same
/// <see cref="FakeHandler"/>, which makes sequencing straightforward.
/// The <see cref="AuthDelegatingHandler"/> BypassClient is lazily created from
/// InnerHandler (= <see cref="FakeHandler"/>), so no real network is touched.
/// </summary>
public sealed class AuthDelegatingHandlerTests : IDisposable
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private const string BaseUrl = "https://node.example.com";

    private readonly string _tempDir;
    private readonly TokenStore _tokenStore;

    public AuthDelegatingHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gunrpg-test-{Guid.NewGuid():N}");
        _tokenStore = new TokenStore(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── LoginAsync tests ────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_RefreshesSilently_WhenStoredTokenValid()
    {
        await _tokenStore.SaveAsync("old-refresh", BaseUrl);

        var newTokens = MakeTokenResponse("new-access", "new-refresh");
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(newTokens)); // refresh response

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        await handler.LoginAsync();

        Assert.Equal("new-access", handler.AccessToken);
        Assert.Contains("/auth/token/refresh", fake.Calls[0].Uri ?? "");

        // Stored refresh token should have been rotated.
        var stored = await _tokenStore.LoadAsync();
        Assert.Equal("new-refresh", stored?.RefreshToken);
    }

    [Fact]
    public async Task LoginAsync_RunsDeviceFlow_WhenNoStoredToken()
    {
        var deviceFlow = MakeDeviceFlow();
        var tokens = MakeTokenResponse("access-from-device", "refresh-from-device");

        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(deviceFlow));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        await handler.LoginAsync();

        Assert.Equal("access-from-device", handler.AccessToken);
        Assert.Equal(2, fake.Calls.Count);
        Assert.Contains("/auth/device/start", fake.Calls[0].Uri ?? "");
        Assert.Contains("/auth/device/poll", fake.Calls[1].Uri ?? "");
    }

    [Fact]
    public async Task LoginAsync_RunsDeviceFlow_WhenRefreshFails()
    {
        await _tokenStore.SaveAsync("expired-refresh", BaseUrl);

        var deviceFlow = MakeDeviceFlow();
        var tokens = MakeTokenResponse("device-access", "device-refresh");

        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.Unauthorized, null); // refresh rejected
        fake.Enqueue(HttpStatusCode.OK, Json(deviceFlow));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        await handler.LoginAsync();

        Assert.Equal("device-access", handler.AccessToken);
        Assert.Equal(3, fake.Calls.Count);
    }

    [Fact]
    public async Task LoginAsync_SkipsRefresh_WhenNodeUrlMismatch()
    {
        // Token stored for a different node; handler should run device flow without trying refresh.
        await _tokenStore.SaveAsync("rt-for-other-node", "https://other-node.example.com");

        var deviceFlow = MakeDeviceFlow();
        var tokens = MakeTokenResponse("da", "dr");

        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, Json(deviceFlow));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        await handler.LoginAsync();

        // Only device flow calls — no refresh attempt.
        Assert.Equal(2, fake.Calls.Count);
        Assert.Contains("/auth/device/start", fake.Calls[0].Uri ?? "");
    }

    // ─── SendAsync pipeline tests ────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_AttachesBearerToken()
    {
        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.OK, "{}");

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        handler.SetAccessToken("my-token");

        await client.GetAsync("/game/operators");

        var authHeader = fake.Calls[0].AuthHeader;
        Assert.Equal("Bearer my-token", authHeader);
    }

    [Fact]
    public async Task SendAsync_Retries_On401_AfterSuccessfulRefresh()
    {
        await _tokenStore.SaveAsync("valid-refresh", BaseUrl);
        var newTokens = MakeTokenResponse("refreshed-access", "new-refresh");

        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.Unauthorized);        // first attempt → 401
        fake.Enqueue(HttpStatusCode.OK, Json(newTokens)); // token refresh call
        fake.Enqueue(HttpStatusCode.OK, "{}");             // retry succeeds

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        handler.SetAccessToken("old-access");

        var response = await client.GetAsync("/game/operators");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("refreshed-access", handler.AccessToken);
        Assert.Equal(3, fake.Calls.Count);
    }

    [Fact]
    public async Task SendAsync_RunsDeviceFlow_On401_WhenRefreshFails()
    {
        // No stored refresh token → device flow is the only path.
        var deviceFlow = MakeDeviceFlow();
        var tokens = MakeTokenResponse("device-access-2", "dr2");

        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.Unauthorized);
        fake.Enqueue(HttpStatusCode.OK, Json(deviceFlow));
        fake.Enqueue(HttpStatusCode.OK, Json(new DevicePollResponse("authorized", tokens)));
        fake.Enqueue(HttpStatusCode.OK, "{}"); // retry succeeds

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };

        var response = await client.GetAsync("/game/operators");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("device-access-2", handler.AccessToken);
        Assert.Equal(4, fake.Calls.Count);
    }

    [Fact]
    public async Task SendAsync_ResendsBody_On401Retry()
    {
        await _tokenStore.SaveAsync("valid-refresh", BaseUrl);
        var newTokens = MakeTokenResponse("refreshed-access", "nr");

        var fake = new FakeHandler();
        fake.Enqueue(HttpStatusCode.Unauthorized); // first POST → 401
        fake.Enqueue(HttpStatusCode.OK, Json(newTokens)); // refresh
        fake.Enqueue(HttpStatusCode.OK, "{}");             // retry POST

        var handler = MakeHandler(fake);
        using var client = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        handler.SetAccessToken("old-access");

        var payload = new StringContent("{\"name\":\"test\"}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/game/operators", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Original attempt and retry must both have carried the request body.
        Assert.NotNull(fake.Calls[0].RequestBody);
        Assert.NotNull(fake.Calls[2].RequestBody);
        Assert.Equal(fake.Calls[0].RequestBody, fake.Calls[2].RequestBody);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private AuthDelegatingHandler MakeHandler(FakeHandler fake) =>
        new(_tokenStore, BaseUrl) { InnerHandler = fake };

    private static string Json<T>(T obj) => JsonSerializer.Serialize(obj, s_json);

    private static DeviceCodeResponse MakeDeviceFlow() => new(
        DeviceCode: "dc-test",
        UserCode: "TEST-CODE",
        VerificationUri: $"{BaseUrl}/verify",
        ExpiresInSeconds: 300,
        PollIntervalSeconds: 0); // instant delay in tests

    private static TokenResponse MakeTokenResponse(
        string access = "access-token", string refresh = "refresh-token") =>
        new(access, refresh,
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(7));

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
