using System.Net.Http.Json;
using GUNRPG.WebClient.Helpers;
using GUNRPG.WebClient.Models;

namespace GUNRPG.WebClient.Services;

public sealed class OperatorService
{
    private readonly ApiClient _api;

    public OperatorService(ApiClient api)
    {
        _api = api;
    }

    public async Task<(List<OperatorSummary>? Data, string? Error)> ListAsync()
    {
        try
        {
            var response = await _api.GetAsync("/operators");
            if (!response.IsSuccessStatusCode)
                return (null, $"Failed to load operators: {response.StatusCode}");

            var data = await response.Content.ReadFromJsonAsync<List<OperatorSummary>>();
            return (data ?? new(), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(OperatorState? Data, string? Error)> GetAsync(Guid id)
    {
        try
        {
            var response = await _api.GetAsync($"/operators/{id}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (null, "Operator not found.");
            if (!response.IsSuccessStatusCode)
                return (null, $"Failed to load operator: {response.StatusCode}");

            var data = await response.Content.ReadFromJsonAsync<OperatorState>();
            return (data, null);
        }
        catch (Exception ex)
        {
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
            return (data, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(Guid? SessionId, string? Error)> StartCombatSessionAsync(Guid operatorId, string playerName)
    {
        try
        {
            // Step 1: Record the combat session start event on the operator aggregate.
            var response = await _api.PostAsync($"/operators/{operatorId}/infil/combat");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to start combat: {response.StatusCode}");
            }

            var sessionId = await response.Content.ReadFromJsonAsync<Guid>();

            // Step 2: Create the actual combat session in the session store so it can be retrieved.
            var sessionRequest = new SessionCreateRequest
            {
                Id = sessionId,
                OperatorId = operatorId,
                PlayerName = playerName
            };

            var createResponse = await _api.PostAsync("/sessions", sessionRequest);
            if (!createResponse.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(createResponse);
                return (null, err ?? $"Failed to create combat session: {createResponse.StatusCode}");
            }

            return (sessionId, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<string?> CompleteInfilAsync(Guid operatorId)
    {
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
}
