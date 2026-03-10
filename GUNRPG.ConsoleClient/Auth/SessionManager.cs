using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.ConsoleClient.Identity;

namespace GUNRPG.ConsoleClient.Auth;

/// <summary>
/// Represents the current authentication state of the console client.
/// </summary>
public enum AuthState
{
    /// <summary>No valid session — the user must log in.</summary>
    NotAuthenticated,
    /// <summary>The device code flow is in progress; waiting for the user to authenticate in the browser.</summary>
    Authenticating,
    /// <summary>A valid access token is held in memory.</summary>
    Authenticated,
}

/// <summary>
/// Orchestrates all authentication for the console client.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Loading <c>session.json</c> from disk and attempting a silent token refresh on startup.</item>
///   <item>Starting the interactive device-code login flow and updating <see cref="State"/> as it progresses.</item>
///   <item>Persisting the new session to <c>session.json</c> on successful login.</item>
///   <item>Deleting <c>session.json</c> and clearing the in-memory token on logout.</item>
/// </list>
///
/// The TUI reads <see cref="State"/>, <see cref="VerificationUrl"/>, and <see cref="UserCode"/>
/// to render the appropriate screen without coupling to any HTTP details.
///
/// Refresh tokens are never logged. Access tokens are never written to disk.
/// </summary>
public sealed class SessionManager
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly SessionStore _store;
    private readonly AuthDelegatingHandler _authHandler;
    private readonly string _baseUrl;

    // Cached bypass client — bypasses AuthDelegatingHandler to avoid recursive 401 handling.
    private HttpClient? _bypassClient;

    // Volatile so reads from the UI render thread always see the latest value
    // written by the background polling task.
    private volatile AuthState _state = AuthState.NotAuthenticated;

    /// <summary>Current authentication state. Thread-safe for read access from the UI thread.</summary>
    public AuthState State => _state;

    /// <summary>
    /// The verification URI to display to the user during the device-code flow.
    /// <see langword="null"/> when not in the <see cref="AuthState.Authenticating"/> state.
    /// </summary>
    public string? VerificationUrl { get; private set; }

    /// <summary>
    /// The short user code to display during the device-code flow.
    /// <see langword="null"/> when not in the <see cref="AuthState.Authenticating"/> state.
    /// </summary>
    public string? UserCode { get; private set; }

    /// <summary>
    /// Error message from the last failed login attempt, or <see langword="null"/> if none.
    /// </summary>
    public string? LoginError { get; private set; }

    public SessionManager(SessionStore store, AuthDelegatingHandler authHandler, string baseUrl)
    {
        _store = store;
        _authHandler = authHandler;
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// Attempts to silently log in using a stored refresh token.
    /// Returns <see langword="true"/> and transitions to <see cref="AuthState.Authenticated"/>
    /// on success; returns <see langword="false"/> if there is no stored session or if the
    /// refresh token is expired/invalid.
    /// </summary>
    public async Task<bool> TryAutoLoginAsync(CancellationToken ct = default)
    {
        var session = await _store.LoadAsync();
        if (session is null)
            return false;

        return await TryRefreshAsync(session.RefreshToken, ct);
    }

    /// <summary>
    /// Starts the interactive device-code login flow in a background task.
    /// Immediately sets <see cref="State"/> to <see cref="AuthState.Authenticating"/>.
    /// <see cref="VerificationUrl"/> and <see cref="UserCode"/> are updated once the server
    /// responds with device-code data.
    /// On success, transitions to <see cref="AuthState.Authenticated"/> and persists the session.
    /// On failure, transitions back to <see cref="AuthState.NotAuthenticated"/> and sets
    /// <see cref="LoginError"/>.
    /// </summary>
    public void StartLogin(CancellationToken ct)
    {
        _state = AuthState.Authenticating;
        LoginError = null;
        VerificationUrl = null;
        UserCode = null;

        // Fire-and-forget: the background task owns all state transitions.
        // The task is not awaited because StartLogin returns immediately so the TUI can
        // render the Authenticating screen while polling runs in the background.
        // CancellationToken propagation ensures the task is cancelled if the app exits.
        var loginTask = Task.Run(() => RunDeviceFlowAsync(ct), ct);
        GC.KeepAlive(loginTask); // suppress CS4014 "not awaited" analysis
    }

    /// <summary>
    /// Logs out the current user: deletes <c>session.json</c>, clears the in-memory
    /// access token, and transitions to <see cref="AuthState.NotAuthenticated"/>.
    /// </summary>
    public void Logout()
    {
        _store.Delete();
        _authHandler.SetAccessToken(null);
        VerificationUrl = null;
        UserCode = null;
        LoginError = null;
        _state = AuthState.NotAuthenticated;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Exchanges a refresh token for a new token pair via the bypass client.
    /// On success sets the in-memory access token, persists the new session, and returns true.
    /// Does not log token values.
    /// </summary>
    private async Task<bool> TryRefreshAsync(string refreshToken, CancellationToken ct)
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

            _authHandler.SetAccessToken(tokens.AccessToken);
            var userId = ExtractSubFromJwt(tokens.AccessToken);
            await _store.SaveAsync(new SessionData(tokens.RefreshToken, userId, DateTimeOffset.UtcNow));
            _state = AuthState.Authenticated;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Runs the full RFC 8628 device authorization flow.
    /// Updates <see cref="VerificationUrl"/> and <see cref="UserCode"/> for the TUI to display,
    /// then polls until authorized, expired, or cancelled.
    /// </summary>
    private async Task RunDeviceFlowAsync(CancellationToken ct)
    {
        try
        {
            var deviceClient = new DeviceAuthClient(BypassClient, _baseUrl);

            var deviceFlow = await deviceClient.StartDeviceFlowAsync(ct);
            VerificationUrl = deviceFlow.VerificationUri;
            UserCode = deviceFlow.UserCode;

            // Poll respects the server-provided interval (DeviceAuthClient handles slow_down back-off).
            var tokens = await deviceClient.PollForTokenAsync(deviceFlow, ct);

            _authHandler.SetAccessToken(tokens.AccessToken);
            var userId = ExtractSubFromJwt(tokens.AccessToken);
            await _store.SaveAsync(new SessionData(tokens.RefreshToken, userId, DateTimeOffset.UtcNow));
            _state = AuthState.Authenticated;
        }
        catch (OperationCanceledException)
        {
            _state = AuthState.NotAuthenticated;
        }
        catch (Exception ex)
        {
            LoginError = ex.Message;
            _state = AuthState.NotAuthenticated;
        }
    }

    /// <summary>
    /// Decodes the <c>sub</c> claim from a JWT access token without verifying the signature.
    /// Returns an empty string if decoding fails.
    /// </summary>
    private static string ExtractSubFromJwt(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
                return string.Empty;

            // Base64url → standard base64, then pad to a multiple of 4.
            var base64 = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');

            var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("sub", out var sub)
                ? sub.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// An <see cref="HttpClient"/> that bypasses <see cref="AuthDelegatingHandler"/>,
    /// going straight to <see cref="DelegatingHandler.InnerHandler"/>. Used for auth-endpoint
    /// calls (token refresh, device flow) to avoid recursive 401 handling.
    /// </summary>
    /// <remarks>
    /// <see cref="DelegatingHandler.InnerHandler"/> is guaranteed non-null because
    /// <see cref="AuthDelegatingHandler.InnerHandler"/> is always set to a
    /// <see cref="HttpClientHandler"/> before <see cref="SessionManager"/> is constructed
    /// (see Program.cs startup).
    /// </remarks>
    private HttpClient BypassClient =>
        _bypassClient ??= new HttpClient(
            _authHandler.InnerHandler
                ?? throw new InvalidOperationException(
                    "AuthDelegatingHandler.InnerHandler must be set before SessionManager is used."),
            disposeHandler: false);
}
