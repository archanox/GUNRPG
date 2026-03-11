using System.Net.Http.Json;
using GUNRPG.WebClient.Helpers;
using GUNRPG.WebClient.Models;

namespace GUNRPG.WebClient.Services;

public sealed class MissionService
{
    private readonly ApiClient _api;
    private readonly OfflineGameplayService _offlineGameplay;

    public MissionService(ApiClient api, OfflineGameplayService offlineGameplay)
    {
        _api = api;
        _offlineGameplay = offlineGameplay;
    }

    public Task<bool> IsLocalSessionAsync(Guid sessionId) => _offlineGameplay.HasLocalCombatSessionAsync(sessionId);

    public async Task<(CombatSession? Data, string? Error)> GetStateAsync(Guid sessionId)
    {
        if (await _offlineGameplay.HasLocalCombatSessionAsync(sessionId))
            return await _offlineGameplay.GetStateAsync(sessionId);

        try
        {
            var response = await _api.GetAsync($"/sessions/{sessionId}/state");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, "Combat session not found.");
            if (!response.IsSuccessStatusCode)
                return (null, $"Failed to load session: {response.StatusCode}");

            var data = await response.Content.ReadFromJsonAsync<CombatSession>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(CombatSession? Data, string? Error)> SubmitIntentAsync(
        Guid sessionId, Guid operatorId, string? primary, string? movement, string? stance = null, string? cover = null)
    {
        if (await _offlineGameplay.HasLocalCombatSessionAsync(sessionId))
            return await _offlineGameplay.SubmitIntentAsync(sessionId, operatorId, primary, movement, stance, cover);

        try
        {
            var request = new IntentRequest
            {
                OperatorId = operatorId,
                Intents = new IntentDto { Primary = primary, Movement = movement, Stance = stance, Cover = cover }
            };

            var response = await _api.PostAsync($"/sessions/{sessionId}/intent", request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to submit intent: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<CombatSession>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(CombatSession? Data, string? Error)> AdvanceAsync(Guid sessionId, Guid operatorId)
    {
        if (await _offlineGameplay.HasLocalCombatSessionAsync(sessionId))
            return await _offlineGameplay.AdvanceAsync(sessionId, operatorId);

        try
        {
            var response = await _api.PostAsync($"/sessions/{sessionId}/advance",
                new { operatorId });

            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to advance: {response.StatusCode}");
            }

            var data = await response.Content.ReadFromJsonAsync<CombatSession>();
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
