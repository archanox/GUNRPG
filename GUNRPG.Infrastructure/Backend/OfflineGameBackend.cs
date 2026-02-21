using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
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

        // Run full combat simulation using the same domain logic as online mode
        Guid? parsedOperatorId = Guid.TryParse(request.OperatorId, out var parsed) ? parsed : null;
        var outcome = CombatSimulationService.RunSimulation(
            playerName: operatorDto.Name,
            operatorId: parsedOperatorId);

        // Map CombatOutcome to MissionResultDto
        var result = new MissionResultDto
        {
            OperatorId = request.OperatorId,
            Victory = outcome.IsVictory,
            XpGained = outcome.XpGained,
            OperatorDied = outcome.OperatorDied,
            TurnsSurvived = outcome.TurnsSurvived,
            DamageTaken = outcome.DamageTaken,
            ResultJson = JsonSerializer.Serialize(new
            {
                operatorId = request.OperatorId,
                sessionId = outcome.SessionId.ToString(),
                executedOffline = true,
                isVictory = outcome.IsVictory,
                operatorDied = outcome.OperatorDied,
                xpGained = outcome.XpGained,
                turnsSurvived = outcome.TurnsSurvived,
                damageTaken = outcome.DamageTaken,
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

        // Update the local operator snapshot with full post-mission state
        operatorDto.TotalXp += result.XpGained;
        if (outcome.OperatorDied)
        {
            operatorDto.IsDead = true;
            operatorDto.CurrentHealth = 0;
            operatorDto.CurrentMode = "Dead";
        }
        else
        {
            operatorDto.CurrentHealth = Math.Max(0, operatorDto.CurrentHealth - outcome.DamageTaken);
            if (outcome.IsVictory)
            {
                operatorDto.ExfilStreak++;
            }
        }
        _offlineStore.UpdateOperatorSnapshot(request.OperatorId, operatorDto);

        var unsyncedCount = _offlineStore.GetUnsyncedResults(request.OperatorId).Count;
        Console.WriteLine($"[OFFLINE] Combat simulation completed for '{operatorDto.Name}': " +
            $"Victory={outcome.IsVictory}, XP={outcome.XpGained}, Turns={outcome.TurnsSurvived}, " +
            $"DamageTaken={outcome.DamageTaken:F1}, Health={operatorDto.CurrentHealth:F1}/{operatorDto.MaxHealth:F1}");
        Console.WriteLine($"[OFFLINE] Unsynced mission results for '{operatorDto.Name}': {unsyncedCount}");
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<bool> OperatorExistsAsync(string id)
    {
        var exists = _offlineStore.GetInfiledOperator(id) != null;
        return Task.FromResult(exists);
    }
}
