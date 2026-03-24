using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Backend;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ILogger<GameBackendResolver> _logger;

    public GameBackendResolver(HttpClient httpClient, OfflineStore offlineStore, JsonSerializerOptions? jsonOptions = null, ILoggerFactory? loggerFactory = null)
    {
        _httpClient = httpClient;
        _offlineStore = offlineStore;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<GameBackendResolver>() ?? NullLogger<GameBackendResolver>.Instance;
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
            var onlineBackend = new OnlineGameBackend(_httpClient, _offlineStore, _jsonOptions,
                _loggerFactory?.CreateLogger<OnlineGameBackend>());

            // If an operator was infiled offline, sync must succeed before allowing online play.
            var activeOp = _offlineStore.GetActiveInfiledOperator();
            if (activeOp != null)
            {
                _logger.LogInformation("[SYNC] Infiled operator {OperatorId} found — synchronizing before returning online.", activeOp.Id);
                IExfilSyncService syncService = new ExfilSyncService(_offlineStore, onlineBackend,
                    _loggerFactory?.CreateLogger<ExfilSyncService>());
                var syncResult = await syncService.SyncAsync(activeOp.Id);
                if (!syncResult.Success)
                {
                    _logger.LogWarning("[SYNC] Sync failed: {FailureReason}. Gameplay blocked until operator is re-infiled.", syncResult.FailureReason);
                    if (syncResult.IsIntegrityFailure)
                    {
                        // Integrity violation is permanent — remove the snapshot so subsequent
                        // calls to ResolveAsync don't loop on the same unresolvable failure.
                        // The operator must re-infil with a clean slate.
                        _offlineStore.RemoveInfiledOperator(activeOp.Id);
                        _logger.LogWarning("[SYNC] Infiled snapshot for operator {OperatorId} removed. Re-infil required.", activeOp.Id);
                    }
                    CurrentMode = GameMode.Blocked;
                    return onlineBackend;
                }

                _logger.LogInformation("[SYNC] Sync succeeded — {EnvelopesSynced} envelope(s) uploaded.", syncResult.EnvelopesSynced);
            }
            else
            {
                // No infiled operator: log any residual unsynced results from other operators.
                LogUnsyncedResults();
            }

            CurrentMode = GameMode.Online;
            _logger.LogInformation("[MODE] Online mode — server is reachable.");
            return onlineBackend;
        }

        if (_offlineStore.HasActiveInfiledOperator())
        {
            CurrentMode = GameMode.Offline;
            var activeOp = _offlineStore.GetActiveInfiledOperator();
            _logger.LogInformation("[MODE] Offline mode — server unreachable, using infiled operator snapshot (ID: {OperatorId}).", activeOp?.Id);
            LogUnsyncedResults();
            return new OfflineGameBackend(_offlineStore);
        }

        CurrentMode = GameMode.Blocked;
        _logger.LogWarning("[MODE] Blocked — server unreachable and no infiled operator available. Gameplay blocked.");
        return new OnlineGameBackend(_httpClient, _offlineStore, _jsonOptions,
            _loggerFactory?.CreateLogger<OnlineGameBackend>());
    }

    /// <summary>
    /// Logs the count of unsynced offline mission results, broken down per operator.
    /// </summary>
    private void LogUnsyncedResults()
    {
        var unsyncedResults = _offlineStore.GetAllUnsyncedResults();
        if (unsyncedResults.Count > 0)
        {
            var perOperator = unsyncedResults
                .GroupBy(r => r.OperatorId)
                .Select(g => $"{g.Key}({g.Count()})");
            _logger.LogInformation("[SYNC] {TotalCount} unsynced offline mission result(s) pending: {PerOperator}",
                unsyncedResults.Count, string.Join(", ", perOperator));
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
            using var response = await _httpClient.GetAsync(
                "health",
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            // Any completed HTTP response (regardless of status code) means the server is reachable.
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[MODE] Server connectivity check failed: {Message}", ex.Message);
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
