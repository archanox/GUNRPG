using System.Net.Http.Json;
using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Persistence;

namespace GUNRPG.Infrastructure.Backend;

/// <summary>
/// Online game backend that delegates to the HTTP API.
/// Also handles infil snapshot persistence to local storage.
/// Combat remains interactive and player-driven via the session-based API — this backend
/// handles operator data access, not gameplay execution.
/// </summary>
public sealed class OnlineGameBackend : IGameBackend
{
    private readonly HttpClient _httpClient;
    private readonly OfflineStore _offlineStore;
    private readonly JsonSerializerOptions _jsonOptions;

    public OnlineGameBackend(HttpClient httpClient, OfflineStore offlineStore, JsonSerializerOptions? jsonOptions = null)
    {
        _httpClient = httpClient;
        _offlineStore = offlineStore;
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    /// <inheritdoc />
    public async Task<OperatorDto?> GetOperatorAsync(string id)
    {
        var response = await _httpClient.GetAsync($"operators/{id}");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        return MapFromApiJson(id, json);
    }

    /// <inheritdoc />
    public async Task<OperatorDto> InfilOperatorAsync(string id)
    {
        // Fetch operator from server
        var response = await _httpClient.GetAsync($"operators/{id}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"Operator {id} not found on server.");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var operatorDto = MapFromApiJson(id, json)
            ?? throw new InvalidOperationException($"Failed to parse operator {id} response from server.");

        // Persist snapshot locally for offline use
        _offlineStore.SaveInfiledOperator(operatorDto);

        Console.WriteLine($"[INFIL] Operator '{operatorDto.Name}' (ID: {id}) infiled successfully.");
        Console.WriteLine($"[INFIL] Snapshot saved — offline play now available if server becomes unreachable.");
        return operatorDto;
    }

    /// <inheritdoc />
    public async Task<bool> OperatorExistsAsync(string id)
    {
        var response = await _httpClient.GetAsync($"operators/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SyncOfflineMission(OfflineMissionEnvelope envelope, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("operators/offline/sync", envelope, _jsonOptions, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Maps API JSON response to an OperatorDto.
    /// </summary>
    private OperatorDto? MapFromApiJson(string id, string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<OperatorDto>(json, _jsonOptions);
            if (dto != null)
            {
                dto.Id = id;
            }
            return dto;
        }
        catch
        {
            return null;
        }
    }
}
