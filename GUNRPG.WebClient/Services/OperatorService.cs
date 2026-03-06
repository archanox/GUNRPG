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
            var response = await _api.GetAsync("/api/operators");
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
            var response = await _api.GetAsync($"/api/operators/{id}");
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
            var response = await _api.PostAsync("/api/operators", new OperatorCreateRequest { Name = name });
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
            var response = await _api.PostAsync($"/api/operators/{operatorId}/infil/start");
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

    public async Task<(Guid? SessionId, string? Error)> StartCombatSessionAsync(Guid operatorId)
    {
        try
        {
            var response = await _api.PostAsync($"/api/operators/{operatorId}/infil/combat");
            if (!response.IsSuccessStatusCode)
            {
                var err = await ApiHelpers.TryReadErrorAsync(response);
                return (null, err ?? $"Failed to start combat: {response.StatusCode}");
            }

            var sessionId = await response.Content.ReadFromJsonAsync<Guid>();
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
            var response = await _api.PostAsync($"/api/operators/{operatorId}/infil/complete");
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
}
