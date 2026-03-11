using GUNRPG.Application.Sessions;
using Microsoft.JSInterop;

namespace GUNRPG.WebClient.Services;

public sealed class BrowserCombatSessionStore : ICombatSessionStore
{
    private readonly IJSRuntime _js;

    public BrowserCombatSessionStore(IJSRuntime js)
    {
        _js = js;
    }

    public Task SaveAsync(CombatSessionSnapshot snapshot) =>
        _js.InvokeVoidAsync("gunRpgStorage.saveCombatSession", snapshot).AsTask();

    public Task<CombatSessionSnapshot?> LoadAsync(Guid id) =>
        _js.InvokeAsync<CombatSessionSnapshot?>("gunRpgStorage.loadCombatSession", id.ToString()).AsTask();

    public Task DeleteAsync(Guid id) =>
        _js.InvokeVoidAsync("gunRpgStorage.deleteCombatSession", id.ToString()).AsTask();

    public async Task<IReadOnlyCollection<CombatSessionSnapshot>> ListAsync()
    {
        var snapshots = await _js.InvokeAsync<List<CombatSessionSnapshot>>("gunRpgStorage.getAllCombatSessions");
        return snapshots;
    }
}
