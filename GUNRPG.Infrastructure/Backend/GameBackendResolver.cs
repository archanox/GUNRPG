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
            return new OnlineGameBackend(_httpClient, _offlineStore, _jsonOptions);
        }

        if (_offlineStore.HasActiveInfiledOperator())
        {
            CurrentMode = GameMode.Offline;
            Console.WriteLine("[MODE] Offline mode — server unreachable, using infiled operator snapshot.");
            return new OfflineGameBackend(_offlineStore);
        }

        CurrentMode = GameMode.Blocked;
        Console.WriteLine("[MODE] Online mode (blocked) — server unreachable and no infiled operator available.");
        // Return online backend, but gameplay will be blocked at the client level
        return new OnlineGameBackend(_httpClient, _offlineStore, _jsonOptions);
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
            return response.IsSuccessStatusCode;
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
