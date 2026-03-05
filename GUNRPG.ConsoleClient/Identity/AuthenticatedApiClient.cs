using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GUNRPG.Application.Identity.Dtos;

namespace GUNRPG.ConsoleClient.Identity;

/// <summary>
/// HTTP client wrapper that automatically attaches a Bearer access token to every
/// request and retries on 401 Unauthorized by refreshing the stored refresh token
/// or, if refresh fails, restarting the interactive device authorization flow.
///
/// Setting the access token also updates the underlying <see cref="HttpClient"/>'s
/// default Authorization header so existing game API calls are authenticated without
/// code changes to the game state layer.
/// </summary>
public sealed class AuthenticatedApiClient
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly TokenStore _tokenStore;
    private readonly DeviceAuthClient _deviceAuth;
    private string? _accessToken;

    public AuthenticatedApiClient(
        HttpClient http,
        string baseUrl,
        string? accessToken,
        TokenStore tokenStore,
        DeviceAuthClient deviceAuth)
    {
        _http = http;
        _baseUrl = baseUrl;
        _tokenStore = tokenStore;
        _deviceAuth = deviceAuth;
        SetAccessToken(accessToken);
    }

    /// <summary>In-memory access token; never written to disk.</summary>
    public string? AccessToken => _accessToken;

    /// <summary>
    /// Updates the in-memory access token and the underlying <see cref="HttpClient"/>'s
    /// default Authorization header so all existing game API calls are authenticated.
    /// </summary>
    public void SetAccessToken(string? accessToken)
    {
        _accessToken = accessToken;
        _http.DefaultRequestHeaders.Authorization = accessToken is not null
            ? new AuthenticationHeaderValue("Bearer", accessToken)
            : null;
    }

    /// <summary>
    /// Runs the startup login flow:
    /// <list type="bullet">
    ///   <item>If a refresh token is stored for this node, attempts a silent refresh.</item>
    ///   <item>If no stored token or refresh fails, starts the interactive device flow.</item>
    /// </list>
    /// </summary>
    public async Task LoginAsync(CancellationToken ct = default)
    {
        var stored = await _tokenStore.LoadAsync();

        if (stored is not null)
        {
            if (stored.NodeUrl == _baseUrl && await TryRefreshAsync(stored.RefreshToken, stored.NodeUrl, ct))
            {
                Console.WriteLine("[AUTH] Token refreshed.");
                return;
            }

            // Stored token is for a different node or refresh failed — discard it.
            _tokenStore.Clear();
        }

        // No valid stored token — run the interactive device authorization flow.
        await RunDeviceFlowAsync(ct);
    }

    public Task<HttpResponseMessage> PostAsync<T>(string path, T body, CancellationToken ct = default)
        => SendWithRetryAsync(HttpMethod.Post, path, JsonContent.Create(body, options: s_jsonOptions), ct);

    public Task<HttpResponseMessage> PostAsync(string path, CancellationToken ct = default)
        => SendWithRetryAsync(HttpMethod.Post, path, content: null, ct);

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
        => SendWithRetryAsync(HttpMethod.Get, path, content: null, ct);

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        var response = await SendAsync(method, path, content, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var stored = await _tokenStore.LoadAsync();
            if (stored is not null && stored.NodeUrl == _baseUrl
                && await TryRefreshAsync(stored.RefreshToken, stored.NodeUrl, ct))
                return await SendAsync(method, path, content, ct);

            // Refresh failed or no valid stored token — restart the device authorization flow.
            _tokenStore.Clear();
            await RunDeviceFlowAsync(ct);
            return await SendAsync(method, path, content, ct);
        }

        return response;
    }

    /// <summary>
    /// Sends a single HTTP request using the underlying HttpClient.
    /// Authentication is supplied via <see cref="HttpClient.DefaultRequestHeaders"/> which
    /// is kept in sync by <see cref="SetAccessToken"/>; no per-request header is needed.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");

        if (content is not null)
            request.Content = content;

        return await _http.SendAsync(request, ct);
    }

    /// <summary>
    /// Attempts to exchange the given refresh token for a new token pair.
    /// Returns true and updates the in-memory access token on success.
    /// Returns false on any failure (network error, invalid token, etc.).
    /// Does not log token values.
    /// </summary>
    private async Task<bool> TryRefreshAsync(
        string refreshToken, string nodeUrl, CancellationToken ct)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{_baseUrl}/auth/token/refresh",
                new RefreshTokenRequest(refreshToken),
                s_jsonOptions,
                ct);

            if (!response.IsSuccessStatusCode)
                return false;

            var tokens = await response.Content
                .ReadFromJsonAsync<TokenResponse>(s_jsonOptions, ct);

            if (tokens is null)
                return false;

            SetAccessToken(tokens.AccessToken);
            await _tokenStore.SaveAsync(tokens.RefreshToken, nodeUrl);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Interactively runs the device authorization flow.
    /// Displays the user code and verification URI on the console, then polls until
    /// the user authorizes in their browser.
    /// </summary>
    private async Task RunDeviceFlowAsync(CancellationToken ct)
    {
        var deviceFlow = await _deviceAuth.StartDeviceFlowAsync(ct);

        Console.WriteLine();
        Console.WriteLine("[AUTH] Open the following URL in your browser to authenticate:");
        Console.WriteLine($"       {deviceFlow.VerificationUri}");
        Console.WriteLine($"       Enter code: {deviceFlow.UserCode}");
        Console.WriteLine("[AUTH] Waiting for authorization...");

        var tokens = await _deviceAuth.PollForTokenAsync(deviceFlow, ct);

        SetAccessToken(tokens.AccessToken);
        await _tokenStore.SaveAsync(tokens.RefreshToken, _baseUrl);
        Console.WriteLine("[AUTH] Authorized.");
    }
}
