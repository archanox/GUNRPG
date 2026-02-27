using System.Globalization;
using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Services;

/// <summary>
/// Application service that orchestrates operator operations and exposes UI-agnostic endpoints.
/// </summary>
public sealed class OperatorService
{
    private readonly OperatorExfilService _exfilService;
    private readonly CombatSessionService _sessionService;
    private readonly IOperatorEventStore _eventStore;
    private readonly IOfflineSyncHeadStore? _offlineSyncHeadStore;
    private readonly IDeterministicCombatEngine? _combatEngine;

    public OperatorService(
        OperatorExfilService exfilService,
        CombatSessionService sessionService,
        IOperatorEventStore eventStore,
        IOfflineSyncHeadStore? offlineSyncHeadStore = null,
        IDeterministicCombatEngine? combatEngine = null)
    {
        _exfilService = exfilService;
        _sessionService = sessionService;
        _eventStore = eventStore;
        _offlineSyncHeadStore = offlineSyncHeadStore;
        _combatEngine = combatEngine;
    }

    public async Task<ServiceResult<OperatorStateDto>> CreateOperatorAsync(OperatorCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<OperatorStateDto>.ValidationError("Operator name cannot be empty");
        }

        var result = await _exfilService.CreateOperatorAsync(request.Name);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(result.Value!);
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    /// <summary>
    /// Gets the current state of an operator, including their active combat session if present.
    /// If the operator is in Infil mode and the 30-minute timer has expired, the infil is
    /// automatically failed before returning state. This enforces the timer server-side so that
    /// no client action is required to transition out of a timed-out infil.
    /// </summary>
    public async Task<ServiceResult<OperatorStateDto>> GetOperatorAsync(Guid operatorId)
    {
        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        var aggregate = loadResult.Value!;

        // Server-authoritative timer enforcement: if the operator is in Infil mode and the
        // 30-minute window has passed, auto-fail the infil here. This mirrors the same guard
        // in StartInfilAsync and ensures that any GET (e.g. RefreshOperator after the client
        // shows the ExfilFailed screen) returns Base-mode state, preventing the client from
        // re-displaying the ExfilFailed dialog on subsequent screens.
        if (aggregate.CurrentMode == OperatorMode.Infil &&
            (!aggregate.InfilStartTime.HasValue ||
             (DateTimeOffset.UtcNow - aggregate.InfilStartTime.Value).TotalMinutes >= OperatorExfilService.InfilTimerMinutes))
        {
            var failResult = await _exfilService.FailInfilAsync(new OperatorId(operatorId), "Infil timer expired (30 minutes)");
            if (!failResult.IsSuccess)
            {
                // If the auto-fail write fails (including due to a concurrent state change),
                // surface the error instead of returning potentially stale Infil state.
                return ServiceResult<OperatorStateDto>.FromResult(failResult);
            }

            // Reload so the returned DTO reflects Base mode
            loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
            if (!loadResult.IsSuccess)
                return ServiceResult<OperatorStateDto>.FromResult(loadResult);
            aggregate = loadResult.Value!;
        }

        var operatorDto = ToDto(aggregate);
        
        // If operator has an active combat session, load and include it
        if (operatorDto.ActiveCombatSessionId.HasValue)
        {
            var sessionResult = await _sessionService.GetStateAsync(operatorDto.ActiveCombatSessionId.Value);
            if (sessionResult.Status == ResultStatus.Success)
            {
                // Session is active (Created, Planning, or Resolving phase), include it in the response
                // Create a new DTO with the active session included
                operatorDto = new OperatorStateDto
                {
                    Id = operatorDto.Id,
                    Name = operatorDto.Name,
                    TotalXp = operatorDto.TotalXp,
                    CurrentHealth = operatorDto.CurrentHealth,
                    MaxHealth = operatorDto.MaxHealth,
                    EquippedWeaponName = operatorDto.EquippedWeaponName,
                    UnlockedPerks = operatorDto.UnlockedPerks,
                    ExfilStreak = operatorDto.ExfilStreak,
                    IsDead = operatorDto.IsDead,
                    CurrentMode = operatorDto.CurrentMode,
                    InfilStartTime = operatorDto.InfilStartTime,
                    InfilSessionId = operatorDto.InfilSessionId,
                    ActiveCombatSessionId = operatorDto.ActiveCombatSessionId,
                    ActiveCombatSession = sessionResult.Value,
                    LockedLoadout = operatorDto.LockedLoadout,
                    Pet = operatorDto.Pet
                };
            }
            else
            {
                // Session lookup failed; clear inconsistent ActiveCombatSessionId in the returned DTO
                // This prevents clients from seeing a dangling session reference
                operatorDto = new OperatorStateDto
                {
                    Id = operatorDto.Id,
                    Name = operatorDto.Name,
                    TotalXp = operatorDto.TotalXp,
                    CurrentHealth = operatorDto.CurrentHealth,
                    MaxHealth = operatorDto.MaxHealth,
                    EquippedWeaponName = operatorDto.EquippedWeaponName,
                    UnlockedPerks = operatorDto.UnlockedPerks,
                    ExfilStreak = operatorDto.ExfilStreak,
                    IsDead = operatorDto.IsDead,
                    CurrentMode = operatorDto.CurrentMode,
                    InfilStartTime = operatorDto.InfilStartTime,
                    InfilSessionId = operatorDto.InfilSessionId,
                    ActiveCombatSessionId = null, // Clear the dangling reference
                    ActiveCombatSession = null,
                    LockedLoadout = operatorDto.LockedLoadout,
                    Pet = operatorDto.Pet
                };
            }
        }

        return ServiceResult<OperatorStateDto>.Success(operatorDto);
    }

    /// <summary>
    /// Cleans up completed combat sessions for an operator.
    /// If the operator has an ActiveCombatSessionId pointing to a Completed session, this method
    /// auto-processes the outcome to prevent the operator from getting stuck.
    /// This should be called before GetOperatorAsync when resuming a saved operator.
    /// </summary>
    public async Task<ServiceResult> CleanupCompletedSessionAsync(Guid operatorId)
    {
        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return loadResult.Status switch
            {
                ResultStatus.NotFound => ServiceResult.NotFound(loadResult.ErrorMessage),
                ResultStatus.InvalidState => ServiceResult.InvalidState(loadResult.ErrorMessage!),
                ResultStatus.ValidationError => ServiceResult.ValidationError(loadResult.ErrorMessage!),
                _ => ServiceResult.InvalidState(loadResult.ErrorMessage!)
            };
        }

        var aggregate = loadResult.Value!;
        
        // If operator has no active combat session, nothing to cleanup
        if (aggregate.ActiveCombatSessionId == null)
        {
            return ServiceResult.Success();
        }

        var sessionResult = await _sessionService.GetStateAsync(aggregate.ActiveCombatSessionId.Value);
        
        // If session not found, the reference is dangling — clear it so the operator can start a new combat.
        // This can happen when a session is deleted without the corresponding CombatVictoryEvent being emitted.
        if (sessionResult.Status != ResultStatus.Success)
        {
            await _exfilService.ClearDanglingCombatSessionAsync(new OperatorId(operatorId));
            return ServiceResult.Success();
        }

        var session = sessionResult.Value!;
        
        // Only process if session is completed
        if (session.Phase != SessionPhase.Completed)
        {
            return ServiceResult.Success();
        }

        // Get the combat outcome
        var outcomeResult = await _sessionService.GetCombatOutcomeAsync(aggregate.ActiveCombatSessionId.Value);
        if (outcomeResult.Status != ResultStatus.Success)
        {
            // Failed to get combat outcome; log and return success to avoid blocking the Get operation
            // The completed session will be included in GetOperatorAsync response for manual recovery
            return ServiceResult.Success();
        }

        // Process the outcome automatically
        var processResult = await _exfilService.ProcessCombatOutcomeAsync(outcomeResult.Value!, playerConfirmed: true);
        if (processResult.Status != ResultStatus.Success)
        {
            // Processing failed; log and return success to avoid blocking the Get operation
            // The operator state may be partially updated, but GetOperatorAsync will return current state
            return ServiceResult.Success();
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<List<OperatorSummaryDto>>> ListOperatorsAsync()
    {
        var operatorIds = await _eventStore.ListOperatorIdsAsync();
        var summaries = new List<OperatorSummaryDto>();

        foreach (var operatorId in operatorIds)
        {
            var loadResult = await _exfilService.LoadOperatorAsync(operatorId);
            if (loadResult.IsSuccess)
            {
                var aggregate = loadResult.Value!;
                summaries.Add(new OperatorSummaryDto
                {
                    Id = aggregate.Id.Value,
                    Name = aggregate.Name,
                    CurrentMode = aggregate.CurrentMode.ToString(),
                    IsDead = aggregate.IsDead,
                    TotalXp = aggregate.TotalXp,
                    CurrentHealth = aggregate.CurrentHealth,
                    MaxHealth = aggregate.MaxHealth
                });
            }
        }

        return ServiceResult<List<OperatorSummaryDto>>.Success(summaries);
    }

    public async Task<ServiceResult<StartInfilResponse>> StartInfilAsync(Guid operatorId)
    {
        // Start the infil - this only transitions the operator to Infil mode
        // Combat sessions are created separately when the player engages in combat
        var result = await _exfilService.StartInfilAsync(new OperatorId(operatorId));
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<StartInfilResponse>.FromResult(result);
        }

        var infilSessionId = result.Value!;

        // Load the operator to return current state
        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            // Operator load failed after infil was started - fail the infil to keep state consistent
            var loadErrorDetails = loadResult.ErrorMessage ?? "Unknown error";
            var failLoadResult = await _exfilService.FailInfilAsync(new OperatorId(operatorId), $"Operator load failed: {loadErrorDetails}");
            if (failLoadResult.Status != ResultStatus.Success)
            {
                var cleanupError = failLoadResult.ErrorMessage ?? "Unknown error while failing infil";
                Console.Error.WriteLine($"Failed to fail infil for operator {operatorId}: {cleanupError}");
            }
            return ServiceResult<StartInfilResponse>.FromResult(loadResult);
        }

        var operatorAggregate = loadResult.Value!;

        // Return the infil session ID (used for tracking the overall infil, not a combat session)
        // Combat sessions will be created when the player engages in combat via the combat endpoint, which calls OperatorExfilService.StartCombatSessionAsync
        return ServiceResult<StartInfilResponse>.Success(new StartInfilResponse
        {
            SessionId = infilSessionId,
            Operator = ToDto(operatorAggregate)
        });
    }

    public async Task<ServiceResult<OperatorStateDto>> ProcessCombatOutcomeAsync(Guid operatorId, ProcessOutcomeRequest request)
    {
        if (request.SessionId == Guid.Empty)
        {
            return ServiceResult<OperatorStateDto>.ValidationError("Session ID cannot be empty");
        }

        var operatorLoadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!operatorLoadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(operatorLoadResult);
        }

        var operatorAggregate = operatorLoadResult.Value!;
        if (operatorAggregate.ActiveCombatSessionId == null)
        {
            return ServiceResult<OperatorStateDto>.InvalidState("No active combat session found for operator");
        }

        if (operatorAggregate.ActiveCombatSessionId.Value != request.SessionId)
        {
            return ServiceResult<OperatorStateDto>.InvalidState("Combat session does not belong to operator");
        }

        // Load the combat session and get the authoritative outcome
        var outcomeResult = await _sessionService.GetCombatOutcomeAsync(request.SessionId);
        if (outcomeResult.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(outcomeResult);
        }

        var outcome = outcomeResult.Value!;

        var result = await _exfilService.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<Guid>> StartCombatSessionAsync(Guid operatorId)
    {
        // Resolve any dangling session reference before attempting to start a new one.
        // If the operator's ActiveCombatSessionId points to a session that no longer exists,
        // CleanupCompletedSessionAsync emits a CombatVictoryEvent to clear the stale reference.
        await CleanupCompletedSessionAsync(operatorId);

        return await _exfilService.StartCombatSessionAsync(new OperatorId(operatorId));
    }

    public async Task<ServiceResult> CompleteInfilAsync(Guid operatorId)
    {
        return await _exfilService.CompleteInfilSuccessfullyAsync(new OperatorId(operatorId));
    }

    public async Task<ServiceResult<OperatorStateDto>> ChangeLoadoutAsync(Guid operatorId, ChangeLoadoutRequest request)
    {
        var result = await _exfilService.ChangeLoadoutAsync(new OperatorId(operatorId), request.WeaponName);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> TreatWoundsAsync(Guid operatorId, TreatWoundsRequest request)
    {
        var result = await _exfilService.TreatWoundsAsync(new OperatorId(operatorId), request.HealthAmount);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> ApplyXpAsync(Guid operatorId, ApplyXpRequest request)
    {
        var result = await _exfilService.ApplyXpAsync(new OperatorId(operatorId), request.XpAmount, request.Reason);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> UnlockPerkAsync(Guid operatorId, UnlockPerkRequest request)
    {
        var result = await _exfilService.UnlockPerkAsync(new OperatorId(operatorId), request.PerkName);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> ApplyPetActionAsync(Guid operatorId, PetActionRequest request)
    {
        var result = await _exfilService.ApplyPetActionAsync(new OperatorId(operatorId), request);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult> SyncOfflineMission(OfflineMissionEnvelope envelope)
    {
        if (!Guid.TryParse(envelope.OperatorId, out var operatorId))
            return ServiceResult.ValidationError("Offline mission envelope has invalid operator ID");
        if (envelope.SequenceNumber <= 0)
            return ServiceResult.ValidationError("Offline mission envelope sequence must be positive");

        // When the engine is injected, require InitialSnapshotJson for server-authoritative replay.
        // Without the engine, fall back to the battle-log-based approach (legacy/test path).
        if (_combatEngine != null && string.IsNullOrEmpty(envelope.InitialSnapshotJson))
            return ServiceResult.ValidationError("Offline mission envelope must include an initial snapshot");
        if (_combatEngine == null && (envelope.FullBattleLog == null || envelope.FullBattleLog.Count == 0))
            return ServiceResult.ValidationError("Offline mission envelope must include a full battle log");

        // _offlineSyncHeadStore remains optional for lightweight test construction paths.
        var previous = _offlineSyncHeadStore == null ? null : await _offlineSyncHeadStore.GetAsync(operatorId);
        if (previous != null)
        {
            if (envelope.SequenceNumber != previous.SequenceNumber + 1)
                return ServiceResult.InvalidState("Offline mission envelope sequence is not contiguous");
            if (!string.Equals(envelope.InitialOperatorStateHash, previous.ResultOperatorStateHash, StringComparison.Ordinal))
                return ServiceResult.InvalidState("Offline mission envelope hash chain is broken");
        }
        else if (envelope.SequenceNumber != 1)
        {
            return ServiceResult.InvalidState("First offline mission envelope must begin at sequence 1");
        }

        // Load raw operator state without the infil timer auto-fail mutation.
        // GetOperatorAsync would transition Infil→Base before returning state, causing a
        // spurious hash mismatch against the first envelope's InitialOperatorStateHash which
        // was computed while the operator was still in Infil mode.
        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return loadResult.Status switch
            {
                ResultStatus.NotFound => ServiceResult.NotFound(loadResult.ErrorMessage),
                ResultStatus.InvalidState => ServiceResult.InvalidState(loadResult.ErrorMessage!),
                ResultStatus.ValidationError => ServiceResult.ValidationError(loadResult.ErrorMessage!),
                _ => ServiceResult.InvalidState(loadResult.ErrorMessage!)
            };
        }

        var currentState = ToDto(loadResult.Value!);
        if (previous == null)
        {
            // First accepted envelope for this operator is anchored to current server operator state.
            // Later envelopes are anchored to the persisted sync head hash chain.
            var currentHash = OfflineMissionHashing.ComputeOperatorStateHash(currentState);
            if (!string.Equals(currentHash, envelope.InitialOperatorStateHash, StringComparison.Ordinal))
                return ServiceResult.InvalidState("Offline mission envelope initial state hash mismatch");
        }

        var replayedState = ReplayOfflineMission(envelope, _combatEngine, currentState);
        if (replayedState == null)
            return ServiceResult.InvalidState("Offline mission envelope initial snapshot could not be deserialized");
        var replayedHash = OfflineMissionHashing.ComputeOperatorStateHash(replayedState);
        if (!string.Equals(replayedHash, envelope.ResultOperatorStateHash, StringComparison.Ordinal))
            return ServiceResult.InvalidState("Offline mission envelope final state hash mismatch");

        if (_offlineSyncHeadStore != null)
        {
            await _offlineSyncHeadStore.UpsertAsync(new OfflineSyncHead
            {
                OperatorId = operatorId,
                SequenceNumber = envelope.SequenceNumber,
                ResultOperatorStateHash = envelope.ResultOperatorStateHash
            });
        }
        return ServiceResult.Success();
    }

    private static readonly JsonSerializerOptions _replayJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Replays an offline mission to produce the authoritative result operator state.
    /// When <paramref name="engine"/> is provided, deserializes <see cref="OfflineMissionEnvelope.InitialSnapshotJson"/>
    /// and executes it deterministically. Falls back to a log-based stub when no engine is injected
    /// (legacy / test paths only).
    /// </summary>
    private static OperatorDto? ReplayOfflineMission(
        OfflineMissionEnvelope envelope,
        IDeterministicCombatEngine? engine,
        OperatorStateDto? fallbackState = null)
    {
        if (engine != null)
        {
            OperatorDto? initialDto;
            try
            {
                initialDto = JsonSerializer.Deserialize<OperatorDto>(envelope.InitialSnapshotJson, _replayJsonOptions);
            }
            catch (JsonException)
            {
                // Malformed InitialSnapshotJson — caller maps null return to InvalidState error.
                return null;
            }

            if (initialDto == null)
                return null;

            var engineResult = engine.Execute(initialDto, envelope.RandomSeed);
            return engineResult.ResultOperator;
        }

        // Fallback (no engine injected): derive result from battle log and server state.
        if (fallbackState == null)
            return null;

        const int victoryXp = 100, survivalXp = 50;
        var damageTaken = (envelope.FullBattleLog ?? [])
            .Where(x => x.EventType == "Damage" && x.Message.Contains($"{fallbackState.Name} took ", StringComparison.Ordinal))
            .Select(ParseDamageAmount)
            .Sum();
        var operatorDied = damageTaken >= fallbackState.CurrentHealth;
        var enemyDamaged = (envelope.FullBattleLog ?? []).Any(x => x.EventType == "Damage" && !x.Message.Contains($"{fallbackState.Name} took ", StringComparison.Ordinal));
        var victory = !operatorDied && enemyDamaged;
        var xpGained = victory ? victoryXp : operatorDied ? 0 : survivalXp;

        return new OperatorDto
        {
            Id = fallbackState.Id.ToString(),
            Name = fallbackState.Name,
            TotalXp = fallbackState.TotalXp + xpGained,
            CurrentHealth = operatorDied ? fallbackState.MaxHealth : Math.Max(1f, fallbackState.CurrentHealth - damageTaken),
            MaxHealth = fallbackState.MaxHealth,
            EquippedWeaponName = fallbackState.EquippedWeaponName,
            UnlockedPerks = fallbackState.UnlockedPerks,
            ExfilStreak = fallbackState.ExfilStreak,
            IsDead = false,
            CurrentMode = operatorDied ? "Base" : "Infil",
            ActiveCombatSessionId = null,
            InfilSessionId = operatorDied ? null : fallbackState.InfilSessionId,
            InfilStartTime = operatorDied ? null : fallbackState.InfilStartTime,
            LockedLoadout = fallbackState.LockedLoadout,
            Pet = fallbackState.Pet
        };
    }

    private static float ParseDamageAmount(BattleLogEntryDto entry)
    {
        var start = entry.Message.IndexOf(" took ", StringComparison.Ordinal);
        if (start < 0)
            return 0f;
        start += " took ".Length;
        var end = entry.Message.IndexOf(" damage", start, StringComparison.Ordinal);
        if (end <= start)
            return 0f;

        return float.TryParse(entry.Message[start..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var damage)
            ? damage
            : 0f;
    }

    private static OperatorStateDto ToDto(OperatorAggregate aggregate)
    {
        return new OperatorStateDto
        {
            Id = aggregate.Id.Value,
            Name = aggregate.Name,
            TotalXp = aggregate.TotalXp,
            CurrentHealth = aggregate.CurrentHealth,
            MaxHealth = aggregate.MaxHealth,
            EquippedWeaponName = aggregate.EquippedWeaponName,
            UnlockedPerks = aggregate.UnlockedPerks.ToList(),
            ExfilStreak = aggregate.ExfilStreak,
            IsDead = aggregate.IsDead,
            CurrentMode = aggregate.CurrentMode,
            InfilStartTime = aggregate.InfilStartTime,
            InfilSessionId = aggregate.InfilSessionId,
            ActiveCombatSessionId = aggregate.ActiveCombatSessionId,
            LockedLoadout = aggregate.LockedLoadout,
            Pet = aggregate.PetState != null ? SessionMapping.ToDto(aggregate.PetState) : null
        };
    }
}

public sealed class StartInfilResponse
{
    public Guid SessionId { get; init; }
    public OperatorStateDto Operator { get; init; } = null!;
}
