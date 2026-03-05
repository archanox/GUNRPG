using System.Net.Http.Json;
using System.Text.Json;
using GUNRPG.Application.Identity.Dtos;

namespace GUNRPG.ConsoleClient.Identity;

/// <summary>
/// Implements the RFC 8628 Device Authorization Grant for console clients.
/// The console cannot perform WebAuthn directly; instead the user authenticates
/// via a browser-based verification URI, and the console polls for the result.
/// </summary>
public sealed class DeviceAuthClient
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public DeviceAuthClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// Starts the device authorization flow.
    /// Returns the device code, user code, and verification URI to display to the user.
    /// </summary>
    public async Task<DeviceCodeResponse> StartDeviceFlowAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"{_baseUrl}/auth/device/start", content: null, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(s_jsonOptions, ct)
            ?? throw new InvalidOperationException("Empty response from /auth/device/start.");
    }

    /// <summary>
    /// Polls the server at the server-provided interval until the device code is
    /// authorized, expired, or denied.
    /// Backs off by 5 seconds on <c>slow_down</c> per RFC 8628 §3.5.
    /// Returns tokens only when the server responds with <c>authorized</c>.
    /// </summary>
    public async Task<TokenResponse> PollForTokenAsync(
        DeviceCodeResponse deviceFlow, CancellationToken ct = default)
    {
        var intervalSeconds = deviceFlow.PollIntervalSeconds;

        while (true)
        {
            // Respect server-provided interval strictly; Task.Delay avoids CPU spin.
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);

            var pollResponse = await _http.PostAsJsonAsync(
                $"{_baseUrl}/auth/device/poll",
                new DevicePollRequest(deviceFlow.DeviceCode),
                s_jsonOptions,
                ct);

            if (!pollResponse.IsSuccessStatusCode)
            {
                var errorBody = await pollResponse.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"Device poll request failed with HTTP {(int)pollResponse.StatusCode}: {errorBody}");
            }

            var result = await pollResponse.Content
                .ReadFromJsonAsync<DevicePollResponse>(s_jsonOptions, ct)
                ?? throw new InvalidOperationException("Empty response from /auth/device/poll.");

            switch (result.Status)
            {
                case "authorized":
                    return result.Tokens
                        ?? throw new InvalidOperationException(
                            "Server responded authorized but returned no tokens.");

                case "authorization_pending":
                    break; // keep polling at current interval

                case "slow_down":
                    intervalSeconds += 5; // RFC 8628 §3.5: add 5 s per slow_down
                    break;

                case "expired_token":
                    throw new InvalidOperationException(
                        "Device code expired. Please restart the login flow.");

                case "access_denied":
                    throw new InvalidOperationException(
                        "Access was denied. Please restart the login flow.");

                default:
                    throw new InvalidOperationException(
                        $"Unknown device poll status: '{result.Status}'.");
            }
        }
    }
}
