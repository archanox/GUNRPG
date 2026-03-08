using Microsoft.JSInterop;

namespace GUNRPG.WebClient.Services;

public sealed class NodeConnectionService
{
    private readonly IJSRuntime _js;
    private string? _baseUrl;
    private bool _initialized;

    public NodeConnectionService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<string?> GetBaseUrlAsync()
    {
        if (!_initialized)
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", "gunrpg_node_url");
            _baseUrl = NormalizeBaseUrl(stored);
            _initialized = true;
        }
        return _baseUrl;
    }

    public async Task SetBaseUrlAsync(string url)
    {
        var normalized = NormalizeBaseUrl(url)
            ?? throw new ArgumentException("Please enter a valid HTTP or HTTPS URL.");

        _baseUrl = normalized;
        await _js.InvokeVoidAsync("localStorage.setItem", "gunrpg_node_url", _baseUrl);
    }

    public async Task ClearAsync()
    {
        _baseUrl = null;
        await _js.InvokeVoidAsync("localStorage.removeItem", "gunrpg_node_url");
    }

    private static string? NormalizeBaseUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme is not ("http" or "https"))
            return null;

        // Preserve any path base (e.g., https://example.com/api) but drop query/fragment and
        // trailing slash to avoid double-slashes when appending API paths.
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };

        var normalized = builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return normalized;
    }
}
