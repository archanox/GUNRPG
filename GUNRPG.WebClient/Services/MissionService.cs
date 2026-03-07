using System.Net.Http.Json;
using GUNRPG.WebClient.Helpers;
using GUNRPG.WebClient.Models;

namespace GUNRPG.WebClient.Services;

public sealed class MissionService
{
    private readonly ApiClient _api;

    public MissionService(ApiClient api)
    {
        _api = api;
    }

    public async Task<(CombatSession? Data, string? Error)> GetStateAsync(Guid sessionId)
    {
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

    public async Task<string?> DeleteAsync(Guid sessionId)
    {
        try
        {
            var response = await _api.DeleteAsync($"/sessions/{sessionId}");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return err ?? $"Failed to delete session: {response.StatusCode}";
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
