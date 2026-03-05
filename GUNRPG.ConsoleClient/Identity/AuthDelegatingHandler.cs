using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GUNRPG.Application.Identity.Dtos;

namespace GUNRPG.ConsoleClient.Identity;

/// <summary>
/// A <see cref="DelegatingHandler"/> that transparently handles authentication for every
/// HTTP request made through the shared <see cref="System.Net.Http.HttpClient"/>.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Attaches the current Bearer access token to each outgoing request.</item>
///   <item>
///     On HTTP 401: attempts a silent token refresh; if that fails, restarts the
///     interactive device authorization flow. The original request is then retried
///     with the new token.
///   </item>
///   <item>
///     Buffers request bodies before the first send so they can be replayed on retry
///     without a double-read / disposed-stream error.
///   </item>
/// </list>
///
/// Because this handler sits inside the <see cref="System.Net.Http.HttpClient"/> pipeline,
/// <em>all</em> callers — including the game-state layer — benefit from 401 recovery
/// without any code changes.
///
/// Auth endpoint calls (token refresh, device flow) are routed through a bypass client
/// that goes directly to <see cref="DelegatingHandler.InnerHandler"/>, avoiding
/// recursion through this handler.
/// </summary>
public sealed class AuthDelegatingHandler : DelegatingHandler
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly TokenStore _tokenStore;
    private readonly string _baseUrl;

    private string? _accessToken;

    // Lazily created after InnerHandler is set (i.e., after construction).
    private HttpClient? _bypassClient;
    private DeviceAuthClient? _deviceAuth;

    public AuthDelegatingHandler(TokenStore tokenStore, string baseUrl)
    {
        _tokenStore = tokenStore;
        _baseUrl = baseUrl;
    }

    /// <summary>In-memory access token; never written to disk.</summary>
    public string? AccessToken => _accessToken;

    /// <summary>Updates the in-memory access token.</summary>
    public void SetAccessToken(string? accessToken) => _accessToken = accessToken;

    /// <summary>
    /// Runs the startup login flow before the game starts.
    /// <list type="bullet">
    ///   <item>If a valid refresh token is stored for this node, refreshes silently.</item>
    ///   <item>Otherwise starts the interactive device authorization flow.</item>
    /// </list>
    /// </summary>
    public async Task LoginAsync(CancellationToken ct = default)
    {
        var stored = await _tokenStore.LoadAsync();

        if (stored is not null && stored.NodeUrl == _baseUrl)
        {
            if (await TryRefreshAsync(stored.RefreshToken, stored.NodeUrl, ct))
            {
                Console.WriteLine("[AUTH] Token refreshed.");
                return;
            }

            // Stored token invalid or for a different node — discard it.
            _tokenStore.Clear();
        }

        // No valid stored token — run the interactive device authorization flow.
        await RunDeviceFlowAsync(ct);
    }

    // -------------------------------------------------------------------------
    // DelegatingHandler pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Intercepts every request to inject the Bearer token and handle 401 responses.
    /// The request body (if any) is buffered before the first send so it can be
    /// replayed in the retry without encountering a disposed or already-read stream.
    /// </summary>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Buffer the request body before the first send so we can retry after 401.
        byte[]? bufferedBody = null;
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? contentHeaders = null;
        if (request.Content is not null)
        {
            bufferedBody = await request.Content.ReadAsByteArrayAsync(ct);
            contentHeaders = request.Content.Headers.ToList();
        }

        ApplyToken(request);
        var response = await base.SendAsync(request, ct);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // Dispose the 401 response before triggering refresh/device flow.
        response.Dispose();

        // Try to obtain a new token.
        var stored = await _tokenStore.LoadAsync();
        var refreshed = stored is not null && stored.NodeUrl == _baseUrl
            && await TryRefreshAsync(stored.RefreshToken, stored.NodeUrl, ct);

        if (!refreshed)
        {
            _tokenStore.Clear();
            await RunDeviceFlowAsync(ct);
        }

        // Build a fresh request for the retry (the original has already been sent and
        // its content may have been consumed / disposed by the pipeline).
        using var retryRequest = CloneRequest(request, bufferedBody, contentHeaders);
        ApplyToken(retryRequest);
        return await base.SendAsync(retryRequest, ct);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ApplyToken(HttpRequestMessage request)
    {
        if (_accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    /// <summary>
    /// Attempts to exchange the given refresh token for a new token pair via the
    /// bypass client (which skips this handler to avoid recursion).
    /// Returns true on success; false on any failure. Does not log token values.
    /// </summary>
    private async Task<bool> TryRefreshAsync(
        string refreshToken, string nodeUrl, CancellationToken ct)
    {
        try
        {
            using var response = await BypassClient.PostAsJsonAsync(
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
    /// Displays the verification URI and user code, then polls until the user
    /// completes authentication in their browser.
    /// </summary>
    private async Task RunDeviceFlowAsync(CancellationToken ct)
    {
        var deviceFlow = await DeviceAuth.StartDeviceFlowAsync(ct);

        Console.WriteLine();
        Console.WriteLine("[AUTH] Open the following URL in your browser to authenticate:");
        Console.WriteLine($"       {deviceFlow.VerificationUri}");
        Console.WriteLine($"       Enter code: {deviceFlow.UserCode}");
        Console.WriteLine("[AUTH] Waiting for authorization...");

        var tokens = await DeviceAuth.PollForTokenAsync(deviceFlow, ct);

        SetAccessToken(tokens.AccessToken);
        await _tokenStore.SaveAsync(tokens.RefreshToken, _baseUrl);
        Console.WriteLine("[AUTH] Authorized.");
    }

    private static HttpRequestMessage CloneRequest(
        HttpRequestMessage original,
        byte[]? bufferedBody,
        IEnumerable<KeyValuePair<string, IEnumerable<string>>>? contentHeaders)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (bufferedBody is not null)
        {
            clone.Content = new ByteArrayContent(bufferedBody);
            if (contentHeaders is not null)
                foreach (var header in contentHeaders)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    /// <summary>
    /// An <see cref="HttpClient"/> that bypasses this handler, going straight to
    /// <see cref="DelegatingHandler.InnerHandler"/>. Used for auth-endpoint calls
    /// (token refresh, device flow) to avoid recursive 401 handling.
    /// </summary>
    private HttpClient BypassClient =>
        _bypassClient ??= new HttpClient(InnerHandler!, disposeHandler: false);

    private DeviceAuthClient DeviceAuth =>
        _deviceAuth ??= new DeviceAuthClient(BypassClient, _baseUrl);
}
