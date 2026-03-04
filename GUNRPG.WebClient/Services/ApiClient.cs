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

    public async Task<HttpResponseMessage> PostAsync<T>(string path, T body)
    {
        var response = await SendAsync(HttpMethod.Post, path, body);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (await _auth.RefreshTokenAsync())
                response = await SendAsync(HttpMethod.Post, path, body);
        }

        return response;
    }

    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        var response = await SendAsync(HttpMethod.Get, path);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (await _auth.RefreshTokenAsync())
                response = await SendAsync(HttpMethod.Get, path);
        }

        return response;
    }

    public async Task<HttpResponseMessage> PostAsync(string path)
    {
        var response = await SendAsync(HttpMethod.Post, path);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            if (await _auth.RefreshTokenAsync())
                response = await SendAsync(HttpMethod.Post, path);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendAsync<T>(HttpMethod method, string path, T? body = default)
    {
        var baseUrl = await _nodeService.GetBaseUrlAsync()
            ?? throw new InvalidOperationException("No node URL configured.");

        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");

        var token = _auth.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return await _http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path)
    {
        var baseUrl = await _nodeService.GetBaseUrlAsync()
            ?? throw new InvalidOperationException("No node URL configured.");

        var request = new HttpRequestMessage(method, $"{baseUrl}{path}");

        var token = _auth.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _http.SendAsync(request);
    }
}
