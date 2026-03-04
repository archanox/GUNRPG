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
            _baseUrl = await _js.InvokeAsync<string?>("localStorage.getItem", "gunrpg_node_url");
            _initialized = true;
        }
        return _baseUrl;
    }

    public async Task SetBaseUrlAsync(string url)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https")
            throw new ArgumentException("Only HTTPS URLs are allowed for node connections.");

        _baseUrl = uri.GetLeftPart(UriPartial.Authority);
        await _js.InvokeVoidAsync("localStorage.setItem", "gunrpg_node_url", _baseUrl);
    }

    public async Task ClearAsync()
    {
        _baseUrl = null;
        await _js.InvokeVoidAsync("localStorage.removeItem", "gunrpg_node_url");
    }
}
