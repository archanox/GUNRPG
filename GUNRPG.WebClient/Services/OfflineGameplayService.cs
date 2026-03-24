using System.Security.Cryptography;
using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Sessions;
using GUNRPG.WebClient.Helpers;

namespace GUNRPG.WebClient.Services;

public sealed class OfflineGameplayService
{
    private readonly BrowserCombatSessionStore _combatStore;
    private readonly BrowserOfflineStore _offlineStore;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public OfflineGameplayService(BrowserCombatSessionStore combatStore, BrowserOfflineStore offlineStore)
    {
        _combatStore = combatStore;
        _offlineStore = offlineStore;
    }

    public async Task<(Guid? SessionId, string? Error)> StartCombatSessionAsync(Guid operatorId)
    {
        var operatorState = await _offlineStore.GetInfiledOperatorAsync(operatorId);
        if (operatorState is null)
            return (null, "No offline infil snapshot is available for this operator.");

        if (!string.Equals(operatorState.CurrentMode, "Infil", StringComparison.OrdinalIgnoreCase))
            return (null, "Operator is not currently deployed in infil mode.");

        var combatService = new CombatSessionService(_combatStore);
        var request = new SessionCreateRequest
        {
            OperatorId = operatorId,
            PlayerName = operatorState.Name,
            Seed = RandomNumberGenerator.GetInt32(int.MaxValue)
        };

        var result = await combatService.CreateSessionAsync(request);
        if (!result.IsSuccess || result.Value is null)
            return (null, result.ErrorMessage ?? "Failed to create combat session.");

        var updatedOperator = CloneOperator(operatorState, result.Value.Id);
        await _offlineStore.UpdateOperatorSnapshotAsync(operatorId, updatedOperator);
        await _offlineStore.ClearOutcomeProcessedAsync(result.Value.Id);
        return (result.Value.Id, null);
    }

    public async Task<bool> HasLocalCombatSessionAsync(Guid sessionId) =>
        await _combatStore.LoadAsync(sessionId) is not null;

    public async Task<(CombatSession? Data, string? Error)> GetStateAsync(Guid sessionId)
    {
        var combatService = new CombatSessionService(_combatStore);
        var result = await combatService.GetStateAsync(sessionId);
        return ToClientResult(result);
    }

    public async Task<(CombatSession? Data, string? Error)> SubmitIntentAsync(Guid sessionId, Guid operatorId, string? primary, string? movement, string? stance, string? cover)
    {
        var combatService = new CombatSessionService(_combatStore);
        var request = new SubmitIntentsRequest
        {
            OperatorId = operatorId,
            Intents = OfflineModelMapper.ToApplicationIntent(primary, movement, stance, cover)
        };

        var result = await combatService.SubmitPlayerIntentsAsync(sessionId, request);
        return ToClientResult(result);
    }

    public async Task<(CombatSession? Data, string? Error)> AdvanceAsync(Guid sessionId, Guid operatorId)
    {
        var combatService = new CombatSessionService(_combatStore);
        var result = await combatService.AdvanceAsync(sessionId, operatorId);
        if (!result.IsSuccess || result.Value is null)
            return ToClientResult(result);

        if (result.Value.Phase == SessionPhase.Completed)
        {
            var outcomeError = await ProcessCombatOutcomeOfflineAsync(sessionId, operatorId, result.Value);
            if (outcomeError is not null)
                return (OfflineModelMapper.ToClientModel(result.Value), outcomeError);
        }

        return ToClientResult(result);
    }

    public async Task<string?> RetreatFromCombatAsync(Guid operatorId)
    {
        var operatorState = await _offlineStore.GetInfiledOperatorAsync(operatorId);
        if (operatorState?.ActiveCombatSessionId is Guid sessionId)
        {
            await _combatStore.DeleteAsync(sessionId);
            await _offlineStore.ClearOutcomeProcessedAsync(sessionId);
        }

        if (operatorState is null)
            return null;

        var updated = CloneOperator(operatorState, null);
        await _offlineStore.UpdateOperatorSnapshotAsync(operatorId, updated);
        return null;
    }

    public Task QueueExfilAsync(Guid operatorId) => _offlineStore.SetPendingExfilAsync(operatorId);

    private async Task<string?> ProcessCombatOutcomeOfflineAsync(Guid sessionId, Guid operatorId, CombatSessionDto completedSession)
    {
        if (await _offlineStore.IsOutcomeProcessedAsync(sessionId))
            return null;

        var operatorState = await _offlineStore.GetInfiledOperatorAsync(operatorId);
        if (operatorState is null)
            return "Offline operator snapshot is missing.";

        var snapshot = await _combatStore.LoadAsync(sessionId);
        if (snapshot is null)
            return "Offline combat session snapshot is missing.";

        var sessionService = new CombatSessionService(_combatStore);
        var initialDto = OfflineModelMapper.ToBackendDto(operatorState);
        var initialHash = OfflineMissionHashing.ComputeOperatorStateHash(initialDto);
        var outcomeResult = await sessionService.GetCombatOutcomeAsync(sessionId);
        if (outcomeResult.Status != GUNRPG.Application.Results.ResultStatus.Success || outcomeResult.Value is null)
            return outcomeResult.ErrorMessage ?? "Offline combat outcome is unavailable.";

        var replay = await OfflineCombatReplay.ReplayAsync(snapshot.ReplayInitialSnapshotJson, snapshot.ReplayTurns);
        var actualCombatHash = OfflineCombatReplay.ComputeCombatSnapshotHash(completedSession);
        var replayCombatHash = OfflineCombatReplay.ComputeCombatSnapshotHash(replay.FinalSession);
        if (!string.Equals(actualCombatHash, replayCombatHash, StringComparison.Ordinal))
            return "Offline combat replay diverged from the recorded session.";

        var updatedDto = OfflineCombatReplay.ProjectOperatorResult(initialDto, outcomeResult.Value);
        var updatedState = CloneOperator(OfflineModelMapper.ToOperatorState(updatedDto), null);
        var resultHash = OfflineMissionHashing.ComputeOperatorStateHash(updatedDto);
        var nextSequence = await _offlineStore.GetNextMissionSequenceAsync(operatorId);

        var envelope = new OfflineMissionEnvelope
        {
            OperatorId = updatedDto.Id,
            SequenceNumber = nextSequence,
            RandomSeed = snapshot.Seed,
            InitialSnapshotJson = OfflineModelMapper.ToCanonicalJson(initialDto, _jsonOptions),
            ResultSnapshotJson = OfflineModelMapper.ToCanonicalJson(updatedDto, _jsonOptions),
            InitialCombatSnapshotJson = snapshot.ReplayInitialSnapshotJson,
            FinalCombatSnapshotHash = replayCombatHash,
            InitialOperatorStateHash = initialHash,
            ResultOperatorStateHash = resultHash,
            ReplayTurns = snapshot.ReplayTurns.ToList(),
            FullBattleLog = completedSession.BattleLog,
            ExecutedUtc = DateTime.UtcNow,
            Synced = false
        };

        await _offlineStore.SaveMissionResultAsync(envelope);
        await _offlineStore.UpdateOperatorSnapshotAsync(operatorId, updatedState);
        await _offlineStore.MarkOutcomeProcessedAsync(sessionId);
        return null;
    }

    private static (CombatSession? Data, string? Error) ToClientResult(GUNRPG.Application.Results.ServiceResult<GUNRPG.Application.Dtos.CombatSessionDto> result)
    {
        if (!result.IsSuccess || result.Value is null)
            return (null, result.ErrorMessage ?? "Combat operation failed.");

        return (OfflineModelMapper.ToClientModel(result.Value), null);
    }

    private static OperatorState CloneOperator(OperatorState state, Guid? activeCombatSessionId = null) => new()
    {
        Id = state.Id,
        Name = state.Name,
        TotalXp = state.TotalXp,
        CurrentHealth = state.CurrentHealth,
        MaxHealth = state.MaxHealth,
        EquippedWeaponName = state.EquippedWeaponName,
        ExfilStreak = state.ExfilStreak,
        IsDead = state.IsDead,
        CurrentMode = state.CurrentMode,
        InfilStartTime = state.InfilStartTime,
        InfilSessionId = state.InfilSessionId,
        ActiveCombatSessionId = activeCombatSessionId,
        ActiveCombatSession = state.ActiveCombatSession,
        LockedLoadout = state.LockedLoadout,
        Pet = state.Pet
    };
}
