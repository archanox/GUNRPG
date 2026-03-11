using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace GUNRPG.WebClient.Services;

public sealed class AuthService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly NodeConnectionService _nodeService;
    private string? _accessToken;

    public bool IsAuthenticated => _accessToken is not null;

    public AuthService(IJSRuntime js, HttpClient http, NodeConnectionService nodeService)
    {
        _js = js;
        _http = http;
        _nodeService = nodeService;
    }

    public async Task<bool> TryRestoreAsync()
    {
        if (_accessToken is not null)
            return true;

        try
        {
            _accessToken = await _js.InvokeAsync<string?>("tokenStorage.getAccessToken");
            if (!string.IsNullOrEmpty(_accessToken))
                return true;

            return await RefreshTokenAsync();
        }
        catch
        {
            return false;
        }
    }

    public string? GetAccessToken() => _accessToken;

    public async Task<string?> GetSseAccessTokenAsync(bool forceRefresh)
    {
        if (!forceRefresh && !string.IsNullOrEmpty(_accessToken))
            return _accessToken;

        if (await RefreshTokenAsync())
            return _accessToken;

        return forceRefresh ? null : _accessToken;
    }

    public async Task SetTokensAsync(string accessToken, string refreshToken)
    {
        _accessToken = accessToken;
        await _js.InvokeVoidAsync("tokenStorage.storeAccessToken", accessToken);
        await _js.InvokeVoidAsync("tokenStorage.storeRefreshToken", refreshToken);
    }

    public async Task<bool> RefreshTokenAsync()
    {
        var refreshToken = await _js.InvokeAsync<string?>("tokenStorage.getRefreshToken");
        if (string.IsNullOrEmpty(refreshToken))
            return false;

        var baseUrl = await _nodeService.GetBaseUrlAsync();
        if (string.IsNullOrEmpty(baseUrl))
            return false;

        try
        {
            var response = await _http.PostAsJsonAsync(
                $"{baseUrl}/auth/token/refresh",
                new { refreshToken });

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode is >= 400 and < 500)
                    await ClearTokensAsync();
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (result is null)
            {
                await ClearTokensAsync();
                return false;
            }

            _accessToken = result.AccessToken;
            await _js.InvokeVoidAsync("tokenStorage.storeAccessToken", result.AccessToken);
            await _js.InvokeVoidAsync("tokenStorage.storeRefreshToken", result.RefreshToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task ClearTokensAsync()
    {
        _accessToken = null;
        await _js.InvokeVoidAsync("tokenStorage.clearTokens");
    }
}

public sealed class TokenResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("accessTokenExpiresAt")]
    public string? AccessTokenExpiresAt { get; set; }

    [JsonPropertyName("refreshTokenExpiresAt")]
    public string? RefreshTokenExpiresAt { get; set; }
}
