using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Backend;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;

namespace GUNRPG.Infrastructure;

/// <summary>
/// Resolves the appropriate game backend (online or offline) based on current state.
/// Mode is determined by operator state, not configuration.
/// </summary>
public sealed class GameBackendResolver
{
    private readonly HttpClient _httpClient;
    private readonly OfflineStore _offlineStore;
    private readonly JsonSerializerOptions _jsonOptions;

    public GameBackendResolver(HttpClient httpClient, OfflineStore offlineStore, JsonSerializerOptions? jsonOptions = null)
    {
        _httpClient = httpClient;
        _offlineStore = offlineStore;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <summary>
    /// The current mode of operation.
    /// </summary>
    public GameMode CurrentMode { get; private set; } = GameMode.Online;

    /// <summary>
    /// Resolves the appropriate backend based on server reachability and local state.
    /// </summary>
    public async Task<IGameBackend> ResolveAsync()
    {
        var serverReachable = await IsServerReachableAsync();

        if (serverReachable)
        {
            CurrentMode = GameMode.Online;
            Console.WriteLine("[MODE] Online mode — server is reachable.");
            LogUnsyncedResults();
            var onlineBackend = new OnlineGameBackend(_httpClient, _offlineStore, _jsonOptions);
            var syncService = new ExfilSyncService(_offlineStore, onlineBackend);
            var syncSuccess = await syncService.SyncPendingAsync();
            if (!syncSuccess)
            {
                Console.WriteLine("[SYNC] Offline envelope sync stopped due to validation failure or server rejection.");
            }
            return onlineBackend;
        }

        if (_offlineStore.HasActiveInfiledOperator())
        {
            CurrentMode = GameMode.Offline;
            var activeOp = _offlineStore.GetActiveInfiledOperator();
            Console.WriteLine($"[MODE] Offline mode — server unreachable, using infiled operator snapshot (ID: {activeOp?.Id}).");
            LogUnsyncedResults();
            return new OfflineGameBackend(_offlineStore);
        }

        CurrentMode = GameMode.Blocked;
        Console.WriteLine("[MODE] Blocked — server unreachable and no infiled operator available. Gameplay blocked.");
        return new OnlineGameBackend(_httpClient, _offlineStore, _jsonOptions);
    }

    /// <summary>
    /// Logs the count of unsynced offline mission results.
    /// </summary>
    private void LogUnsyncedResults()
    {
        var unsyncedResults = _offlineStore.GetAllUnsyncedResults();
        if (unsyncedResults.Count > 0)
        {
            Console.WriteLine($"[SYNC] {unsyncedResults.Count} unsynced offline mission result(s) pending.");
        }
    }

    /// <summary>
    /// Checks if the API server is reachable.
    /// </summary>
    public async Task<bool> IsServerReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var response = await _httpClient.GetAsync("operators", cts.Token);
            // Any completed HTTP response (regardless of status code) means the server is reachable.
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MODE] Server connectivity check failed: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// Represents the current game mode.
/// </summary>
public enum GameMode
{
    /// <summary>Server reachable, full functionality available.</summary>
    Online,
    /// <summary>Server unreachable, using infiled operator snapshot.</summary>
    Offline,
    /// <summary>Server unreachable, no infiled operator — gameplay blocked.</summary>
    Blocked
}
