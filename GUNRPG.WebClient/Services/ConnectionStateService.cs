using Microsoft.JSInterop;

namespace GUNRPG.WebClient.Services;

public sealed class ConnectionStateService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private DotNetObjectReference<ConnectionStateService>? _dotNetRef;
    private readonly string _subscriptionId = Guid.NewGuid().ToString("N");
    private bool _initialized;

    public ConnectionStateService(IJSRuntime js)
    {
        _js = js;
    }

    public bool IsOnline { get; private set; } = true;

    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        IsOnline = await _js.InvokeAsync<bool>("gunRpgConnection.isOnline");
        _dotNetRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("gunRpgConnection.subscribe", _subscriptionId, _dotNetRef, nameof(OnConnectionChanged));
        _initialized = true;
    }

    [JSInvokable]
    public Task OnConnectionChanged(bool isOnline)
    {
        IsOnline = isOnline;
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            try
            {
                await _js.InvokeVoidAsync("gunRpgConnection.unsubscribe", _subscriptionId);
            }
            catch
            {
                // Ignore disposal-time JS errors.
            }
        }

        _dotNetRef?.Dispose();
    }
}
