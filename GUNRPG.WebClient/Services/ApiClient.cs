using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GUNRPG.WebClient.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly NodeConnectionService _nodeService;
    private readonly AuthService _auth;

    public ApiClient(HttpClient http, NodeConnectionService nodeService, AuthService auth)
    {
        _http = http;
        _nodeService = nodeService;
        _auth = auth;
    }

    public Task<HttpResponseMessage> PostAsync<T>(string path, T body) =>
        SendWithRetryAsync(HttpMethod.Post, path, JsonContent.Create(body));

    public Task<HttpResponseMessage> PostAsync(string path) =>
        SendWithRetryAsync(HttpMethod.Post, path);

    public Task<HttpResponseMessage> GetAsync(string path) =>
        SendWithRetryAsync(HttpMethod.Get, path);

    public Task<HttpResponseMessage> DeleteAsync(string path) =>
        SendWithRetryAsync(HttpMethod.Delete, path);

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method, string path, HttpContent? content = null)
    {
        var response = await SendAsync(method, path, content);

        if (response.StatusCode == HttpStatusCode.Unauthorized && await _auth.RefreshTokenAsync())
            response = await SendAsync(method, path, content);

        return response;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        var baseUrl = await _nodeService.GetBaseUrlAsync()
            ?? throw new InvalidOperationException("No node URL configured.");

        using var request = new HttpRequestMessage(method, $"{baseUrl}{path}");

        var token = _auth.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (content is not null)
            request.Content = content;

        return await _http.SendAsync(request);
    }
}
