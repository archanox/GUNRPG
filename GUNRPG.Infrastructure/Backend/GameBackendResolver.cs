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
    /// When the server is reachable and an infiled operator exists, sync is run first.
    /// Gameplay is blocked if sync fails (chain-of-trust enforcement).
    /// </summary>
    public async Task<IGameBackend> ResolveAsync()
    {
        var serverReachable = await IsServerReachableAsync();

        if (serverReachable)
        {
            var onlineBackend = new OnlineGameBackend(_httpClient, _offlineStore, _jsonOptions);

            // If an operator was infiled offline, sync must succeed before allowing online play.
            var activeOp = _offlineStore.GetActiveInfiledOperator();
            if (activeOp != null)
            {
                Console.WriteLine($"[SYNC] Infiled operator {activeOp.Id} found — synchronizing before returning online.");
                IExfilSyncService syncService = new ExfilSyncService(_offlineStore, onlineBackend);
                var syncResult = await syncService.SyncAsync(activeOp.Id);
                if (!syncResult.Success)
                {
                    Console.WriteLine($"[SYNC] Sync failed: {syncResult.FailureReason}. Gameplay blocked until operator is re-infiled.");
                    CurrentMode = GameMode.Blocked;
                    return onlineBackend;
                }

                Console.WriteLine($"[SYNC] Sync succeeded — {syncResult.EnvelopesSynced} envelope(s) uploaded.");
            }
            else
            {
                // No infiled operator: log any residual unsynced results from other operators.
                LogUnsyncedResults();
            }

            CurrentMode = GameMode.Online;
            Console.WriteLine("[MODE] Online mode — server is reachable.");
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
