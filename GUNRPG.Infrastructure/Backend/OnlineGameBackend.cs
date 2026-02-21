using System.Net.Http.Json;
using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Persistence;

namespace GUNRPG.Infrastructure.Backend;

/// <summary>
/// Online game backend that delegates to the HTTP API.
/// Also handles infill snapshot persistence to local storage.
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
    public async Task<OperatorDto> InfillOperatorAsync(string id)
    {
        // Fetch operator from server
        var response = await _httpClient.GetAsync($"operators/{id}");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var operatorDto = MapFromApiJson(id, json)
            ?? throw new InvalidOperationException($"Operator {id} not found on server.");

        // Persist snapshot locally for offline use
        _offlineStore.SaveInfilledOperator(operatorDto);

        Console.WriteLine($"[ONLINE] Operator '{operatorDto.Name}' infilled and snapshot saved for offline play.");
        return operatorDto;
    }

    /// <inheritdoc />
    public async Task<MissionResultDto> ExecuteMissionAsync(MissionRequest request)
    {
        // In online mode, mission execution goes through the full API workflow.
        // This is a simplified representation - the actual combat loop still uses
        // the session-based API endpoints in the console client.
        var response = await _httpClient.PostAsJsonAsync(
            $"operators/{request.OperatorId}/infil/outcome",
            new { sessionId = request.SessionId },
            _jsonOptions);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return new MissionResultDto
        {
            OperatorId = request.OperatorId,
            Victory = true,
            ResultJson = json
        };
    }

    /// <inheritdoc />
    public async Task<bool> OperatorExistsAsync(string id)
    {
        var response = await _httpClient.GetAsync($"operators/{id}");
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Maps API JSON response to an OperatorDto.
    /// </summary>
    private OperatorDto? MapFromApiJson(string id, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new OperatorDto
            {
                Id = id,
                Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                TotalXp = root.TryGetProperty("totalXp", out var xp) ? xp.GetInt64() : 0,
                CurrentHealth = root.TryGetProperty("currentHealth", out var hp) ? hp.GetSingle() : 0,
                MaxHealth = root.TryGetProperty("maxHealth", out var maxHp) ? maxHp.GetSingle() : 100,
                EquippedWeaponName = root.TryGetProperty("equippedWeaponName", out var weapon) ? weapon.GetString() ?? string.Empty : string.Empty,
                UnlockedPerks = root.TryGetProperty("unlockedPerks", out var perks) && perks.ValueKind == JsonValueKind.Array
                    ? perks.EnumerateArray().Select(p => p.GetString() ?? string.Empty).ToList()
                    : new List<string>(),
                ExfilStreak = root.TryGetProperty("exfilStreak", out var streak) ? streak.GetInt32() : 0,
                IsDead = root.TryGetProperty("isDead", out var dead) && dead.GetBoolean(),
                CurrentMode = root.TryGetProperty("currentMode", out var mode) ? mode.GetString() ?? string.Empty : string.Empty
            };
        }
        catch
        {
            return null;
        }
    }
}
