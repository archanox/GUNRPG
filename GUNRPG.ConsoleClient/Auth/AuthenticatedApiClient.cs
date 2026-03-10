namespace GUNRPG.ConsoleClient.Auth;

/// <summary>
/// Provides the shared <see cref="System.Net.Http.HttpClient"/> instance that has
/// <see cref="GUNRPG.ConsoleClient.Identity.AuthDelegatingHandler"/> attached.
///
/// The handler injects Bearer tokens and transparently handles 401 responses
/// (silent refresh → device-code re-login), so all callers benefit from
/// authenticated API access without any per-call auth logic.
///
/// Game code should use <see cref="Http"/> to make API calls rather than
/// constructing raw <see cref="System.Net.Http.HttpClient"/> instances.
/// </summary>
public sealed class AuthenticatedApiClient
{
    /// <summary>The authenticated HTTP client. Use this to make game API calls.</summary>
    public HttpClient Http { get; }

    public AuthenticatedApiClient(HttpClient http)
    {
        Http = http;
    }
}
