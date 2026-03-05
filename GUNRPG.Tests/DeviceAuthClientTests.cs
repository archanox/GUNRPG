using System.Net;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.ConsoleClient.Identity;

namespace GUNRPG.Tests;

public sealed class DeviceAuthClientTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
    private const string BaseUrl = "https://node.example.com";

    [Fact]
    public async Task StartDeviceFlowAsync_DeserializesResponse()
    {
        var expected = new DeviceCodeResponse(
            DeviceCode: "dc-123",
            UserCode: "ABCD-EFGH",
            VerificationUri: "https://node.example.com/auth/device/verify",
            ExpiresInSeconds: 300,
            PollIntervalSeconds: 0);

        var handler = new FakeHandler();
        handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(expected, s_json));
        using var http = new HttpClient(handler);

        var client = new DeviceAuthClient(http, BaseUrl);
        var result = await client.StartDeviceFlowAsync();

        Assert.Equal("dc-123", result.DeviceCode);
        Assert.Equal("ABCD-EFGH", result.UserCode);
        Assert.Equal(0, result.PollIntervalSeconds);
    }

    [Fact]
    public async Task PollForTokenAsync_ReturnsTokens_WhenImmediatelyAuthorized()
    {
        var tokens = MakeTokenResponse();
        var deviceFlow = MakeDeviceFlow();
        var pollJson = JsonSerializer.Serialize(
            new DevicePollResponse("authorized", tokens), s_json);

        var handler = new FakeHandler();
        handler.Enqueue(HttpStatusCode.OK, pollJson);
        using var http = new HttpClient(handler);

        var client = new DeviceAuthClient(http, BaseUrl);
        var result = await client.PollForTokenAsync(deviceFlow);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
    }

    [Fact]
    public async Task PollForTokenAsync_Throws_WhenExpiredToken()
    {
        var deviceFlow = MakeDeviceFlow();
        var pendingJson = JsonSerializer.Serialize(
            new DevicePollResponse("expired_token", null), s_json);

        var handler = new FakeHandler();
        handler.Enqueue(HttpStatusCode.OK, pendingJson);
        using var http = new HttpClient(handler);

        var client = new DeviceAuthClient(http, BaseUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PollForTokenAsync(deviceFlow));
    }

    [Fact]
    public async Task PollForTokenAsync_Throws_WhenAccessDenied()
    {
        var deviceFlow = MakeDeviceFlow();
        var deniedJson = JsonSerializer.Serialize(
            new DevicePollResponse("access_denied", null), s_json);

        var handler = new FakeHandler();
        handler.Enqueue(HttpStatusCode.OK, deniedJson);
        using var http = new HttpClient(handler);

        var client = new DeviceAuthClient(http, BaseUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PollForTokenAsync(deviceFlow));
    }

    [Fact]
    public async Task PollForTokenAsync_EventuallyAuthorized_AfterPendingAndSlowDown()
    {
        // Verifies the loop continues through authorization_pending and slow_down,
        // and resolves when the server returns "authorized".
        var tokens = MakeTokenResponse();
        var deviceFlow = MakeDeviceFlow();

        var handler = new FakeHandler();
        handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(
            new DevicePollResponse("authorization_pending", null), s_json));
        handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(
            new DevicePollResponse("slow_down", null), s_json));
        handler.Enqueue(HttpStatusCode.OK, JsonSerializer.Serialize(
            new DevicePollResponse("authorized", tokens), s_json));
        using var http = new HttpClient(handler);

        var client = new DeviceAuthClient(http, BaseUrl);
        var result = await client.PollForTokenAsync(deviceFlow);

        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal(3, handler.Calls.Count); // pending + slow_down + authorized
    }

    [Fact]
    public async Task PollForTokenAsync_Throws_WhenServerReturnsErrorStatus()
    {
        var deviceFlow = MakeDeviceFlow();
        var handler = new FakeHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "Server error");
        using var http = new HttpClient(handler);

        var client = new DeviceAuthClient(http, BaseUrl);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PollForTokenAsync(deviceFlow));
        Assert.Contains("500", ex.Message);
        Assert.Contains("Server error", ex.Message);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static DeviceCodeResponse MakeDeviceFlow() => new(
        DeviceCode: "dc-test",
        UserCode: "TEST-CODE",
        VerificationUri: "https://node.example.com/verify",
        ExpiresInSeconds: 300,
        PollIntervalSeconds: 0); // use 0 so Task.Delay is instant in tests

    private static TokenResponse MakeTokenResponse() => new(
        AccessToken: "access-token",
        RefreshToken: "refresh-token",
        AccessTokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15),
        RefreshTokenExpiresAt: DateTimeOffset.UtcNow.AddDays(7));

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string? Body)> _queue = new();
        public List<(HttpMethod Method, string? Uri)> Calls { get; } = new();

        public void Enqueue(HttpStatusCode status, string? body = null)
            => _queue.Enqueue((status, body));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.Method, request.RequestUri?.ToString()));
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
