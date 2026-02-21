using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Persistence;

namespace GUNRPG.Infrastructure.Backend;

/// <summary>
/// Offline game backend that uses local LiteDB for operator state and mission execution.
/// Only available when an operator has been previously infiled from the server.
/// </summary>
public sealed class OfflineGameBackend : IGameBackend
{
    private readonly OfflineStore _offlineStore;

    public OfflineGameBackend(OfflineStore offlineStore)
    {
        _offlineStore = offlineStore;
    }

    /// <inheritdoc />
    public Task<OperatorDto?> GetOperatorAsync(string id)
    {
        var infiled = _offlineStore.GetInfiledOperator(id);
        if (infiled == null)
            return Task.FromResult<OperatorDto?>(null);

        var dto = JsonSerializer.Deserialize<OperatorDto>(infiled.SnapshotJson);
        return Task.FromResult<OperatorDto?>(dto);
    }

    /// <inheritdoc />
    public Task<OperatorDto> InfilOperatorAsync(string id)
    {
        // Infil is not available in offline mode - requires server connection
        throw new InvalidOperationException(
            "[OFFLINE] Cannot infil operator while offline. Server connection required.");
    }

    /// <inheritdoc />
    public Task<MissionResultDto> ExecuteMissionAsync(MissionRequest request)
    {
        var infiled = _offlineStore.GetInfiledOperator(request.OperatorId);
        if (infiled == null)
        {
            throw new InvalidOperationException(
                $"[OFFLINE] Operator {request.OperatorId} has no infiled snapshot. Cannot execute mission offline.");
        }

        var operatorDto = JsonSerializer.Deserialize<OperatorDto>(infiled.SnapshotJson)
            ?? throw new InvalidOperationException("Failed to deserialize operator snapshot.");

        // Simulate a basic mission result offline
        var result = new MissionResultDto
        {
            OperatorId = request.OperatorId,
            Victory = true,
            XpGained = 10,
            ResultJson = JsonSerializer.Serialize(new
            {
                operatorId = request.OperatorId,
                sessionId = request.SessionId,
                executedOffline = true,
                timestamp = DateTime.UtcNow
            })
        };

        // Persist the offline mission result
        var offlineResult = new OfflineMissionResult
        {
            OperatorId = request.OperatorId,
            ResultJson = result.ResultJson,
            ExecutedUtc = DateTime.UtcNow,
            Synced = false
        };
        _offlineStore.SaveMissionResult(offlineResult);

        // Update the local operator snapshot with any changes
        operatorDto.TotalXp += result.XpGained;
        if (result.Victory)
        {
            operatorDto.ExfilStreak++;
        }
        _offlineStore.UpdateOperatorSnapshot(request.OperatorId, operatorDto);

        Console.WriteLine($"[OFFLINE] Mission executed locally for operator '{operatorDto.Name}'. Result persisted for sync.");
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> OperatorExistsAsync(string id)
    {
        var exists = _offlineStore.GetInfiledOperator(id) != null;
        return Task.FromResult(exists);
    }
}
