using System.Net.Http.Json;
using GUNRPG.ClientModels;
using GUNRPG.WebClient.Helpers;
using GUNRPG.WebClient.Models;

namespace GUNRPG.WebClient.Services;

public sealed class OperatorService
{
    private readonly ApiClient _api;
    private readonly BrowserOfflineStore _offlineStore;
    private readonly OfflineGameplayService _offlineGameplay;
    private readonly OfflineSyncService _offlineSync;
    private readonly ConnectionStateService _connection;

    public OperatorService(
        ApiClient api,
        BrowserOfflineStore offlineStore,
        OfflineGameplayService offlineGameplay,
        OfflineSyncService offlineSync,
        ConnectionStateService connection)
    {
        _api = api;
        _offlineStore = offlineStore;
        _offlineGameplay = offlineGameplay;
        _offlineSync = offlineSync;
        _connection = connection;
    }

    public async Task<(List<OperatorSummary>? Data, string? Error)> ListAsync()
    {
        await TrySyncIfOnlineAsync();

        var local = await _offlineStore.GetActiveInfiledOperatorAsync();
        try
        {
            var response = await _api.GetAsync("/operators");
            if (!response.IsSuccessStatusCode)
            {
                if (local is not null)
                    return (new List<OperatorSummary> { OfflineModelMapper.ToSummary(local) }, null);
                return (null, $"Failed to load operators: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<List<OperatorSummary>>() ?? new();
            if (local is not null)
            {
                var summary = OfflineModelMapper.ToSummary(local);
                var index = data.FindIndex(x => x.Id == summary.Id);
                if (index >= 0)
                    data[index] = summary;
                else
                    data.Insert(0, summary);
            }

            return (data, null);
        }
        catch (Exception ex)
        {
            if (local is not null)
                return (new List<OperatorSummary> { OfflineModelMapper.ToSummary(local) }, null);
            return (null, ex.Message);
        }
    }

    public async Task<(OperatorState? Data, string? Error)> GetAsync(Guid id)
    {
        await TrySyncIfOnlineAsync(id);

        var local = await _offlineStore.GetInfiledOperatorAsync(id);
        if (local is not null && !_connection.IsOnline)
            return (local, null);

        try
        {
            var response = await _api.GetAsync($"/operators/{id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, "Operator not found.");
            if (!response.IsSuccessStatusCode)
            {
                if (local is not null)
                    return (local, null);

                return (null, $"Failed to load operator: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<OperatorState>();

            // Reconcile the offline snapshot with the server state: if the operator is still
            // in Infil mode, refresh the local snapshot; otherwise, remove the stale snapshot.
            // Wrapped in its own try/catch so a storage failure (e.g., browser storage blocked)
            // does not prevent the successful server response from being returned.
            if (local is not null && data is not null)
            {
                try
                {
                    if (string.Equals(data.CurrentMode, "Infil", StringComparison.OrdinalIgnoreCase))
                        await _offlineStore.UpdateOperatorSnapshotAsync(id, data);
                    else
                        await _offlineStore.RemoveInfiledOperatorAsync(id);
                }
                catch
                {
                    // Ignore failures in offline reconciliation so that a successful server response
                    // is still returned even if browser storage APIs are unavailable.
                }
            }

            return (data, null);
        }
        catch (Exception ex)
        {
            // Network error — fall back to the local snapshot when available.
            if (local is not null)
                return (local, null);
            return (null, ex.Message);
        }
    }

    public async Task<(OperatorState? Data, string? Error)> CreateAsync(string name)
    {
        try
        {
            var response = await _api.PostAsync("/operators", new OperatorCreateRequest { Name = name });
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to create operator: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<OperatorState>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(StartInfilResponse? Data, string? Error)> StartInfilAsync(Guid operatorId)
    {
        try
        {
            var response = await _api.PostAsync($"/operators/{operatorId}/infil/start");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to start infil: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<StartInfilResponse>();
            if (data?.Operator is not null)
                await _offlineStore.SaveInfiledOperatorAsync(data.Operator);
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(Guid? SessionId, string? Error)> StartCombatSessionAsync(Guid operatorId)
    {
        if (_connection.IsOnline)
        {
            try
            {
                var response = await _api.PostAsync($"/operators/{operatorId}/infil/combat");
                if (!response.IsSuccessStatusCode)
                {
                    var err = await ApiHelpers.TryReadErrorAsync(response);
                    return (null, err ?? $"Failed to start combat: {response.StatusCode}");
                }

                var sessionId = await response.Content.ReadFromJsonAsync<Guid>();
                if (sessionId == Guid.Empty)
                    return (null, "Server returned an invalid combat session ID.");

                await TryUpdateActiveCombatSessionIdAsync(operatorId, sessionId);

                return (sessionId, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        var local = await _offlineStore.GetInfiledOperatorAsync(operatorId);
        if (local?.ActiveCombatSessionId is Guid existingSessionId)
            return (existingSessionId, null);

        return await _offlineGameplay.StartCombatSessionAsync(operatorId);
    }

    public async Task<string?> CompleteInfilAsync(Guid operatorId)
    {
        var local = await _offlineStore.GetInfiledOperatorAsync(operatorId);
        if (local is not null)
        {
            if (!_connection.IsOnline)
            {
                await _offlineGameplay.QueueExfilAsync(operatorId);
                return null;
            }

            // Sync any pending offline combat turns first, and finalize if there is a queued offline exfil.
            var syncError = await _offlineSync.SyncAndFinalizeAsync(operatorId);
            if (syncError is not null)
                return syncError;

            // SyncAndFinalizeAsync may remove the local snapshot either when it processes a queued offline exfil
            // or when the snapshot has already been reconciled to Base (e.g. CurrentMode is already Base).
            // In those cases there is nothing left to exfil, so if the snapshot is gone we can return early here.
            if (await _offlineStore.GetInfiledOperatorAsync(operatorId) is null)
                return null;
        }

        try
        {
            var response = await _api.PostAsync($"/operators/{operatorId}/infil/complete");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return err ?? $"Exfil failed: {response.StatusCode}";
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<string?> RetreatFromCombatAsync(Guid operatorId)
    {
        var local = await _offlineStore.GetInfiledOperatorAsync(operatorId);
        if (local?.ActiveCombatSessionId is not null)
            return await _offlineGameplay.RetreatFromCombatAsync(operatorId);

        try
        {
            var response = await _api.PostAsync($"/operators/{operatorId}/infil/retreat");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return err ?? $"Failed to retreat from combat: {response.StatusCode}";
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<bool> HasQueuedExfilAsync(Guid operatorId) => await _offlineStore.HasPendingExfilAsync(operatorId);

    /// <summary>
    /// Clears the active combat session ID from the local offline snapshot when a server-side session
    /// concludes. This prevents the stale snapshot from causing MissionInfil to redirect to an
    /// unreachable concluded session after the user goes offline.
    /// </summary>
    public async Task ClearActiveCombatSessionAsync(Guid operatorId)
    {
        try
        {
            await _offlineStore.UpdateActiveCombatSessionIdAsync(operatorId, null);
        }
        catch
        {
            // Non-critical: a stale snapshot is only an issue for offline navigation.
        }
    }

    public async Task<(OperatorState? Data, string? Error)> ChangeLoadoutAsync(Guid operatorId, string weaponName)
    {
        try
        {
            var response = await _api.PostAsync($"/operators/{operatorId}/loadout", new ChangeLoadoutRequest { WeaponName = weaponName });
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to change loadout: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<OperatorState>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(OperatorState? Data, string? Error)> TreatWoundsAsync(Guid operatorId, float healthAmount)
    {
        try
        {
            var response = await _api.PostAsync($"/operators/{operatorId}/wounds/treat", new TreatWoundsRequest { HealthAmount = healthAmount });
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to treat wounds: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<OperatorState>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(OperatorState? Data, string? Error)> UnlockPerkAsync(Guid operatorId, string perkName)
    {
        try
        {
            var response = await _api.PostAsync($"/operators/{operatorId}/perks", new UnlockPerkRequest { PerkName = perkName });
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to unlock perk: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<OperatorState>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(OperatorState? Data, string? Error)> ApplyPetActionAsync(Guid operatorId, string action, float? hours = null, float? nutrition = null, float? hydration = null)
    {
        try
        {
            var request = new PetActionRequest
            {
                Action = action,
                Hours = hours,
                Nutrition = nutrition,
                Hydration = hydration
            };
            var response = await _api.PostAsync($"/operators/{operatorId}/pet", request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to apply pet action: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<OperatorState>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private async Task TrySyncIfOnlineAsync(Guid? operatorId = null)
    {
        if (!_connection.IsOnline)
            return;

        try
        {
            if (operatorId.HasValue)
                await _offlineSync.SyncAndFinalizeAsync(operatorId.Value);
            else
                await _offlineSync.TrySyncAllAsync();
        }
        catch
        {
            // Sync failures are surfaced when the player explicitly finalizes exfil.
        }
    }

    private async Task TryUpdateActiveCombatSessionIdAsync(Guid operatorId, Guid sessionId)
    {
        try
        {
            await _offlineStore.UpdateActiveCombatSessionIdAsync(operatorId, sessionId);
        }
        catch
        {
            // Local storage is optional for the online path; server-backed combat can continue
            // even when Safari/iOS blocks IndexedDB or other browser storage APIs.
        }
    }
}
