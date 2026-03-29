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
    private readonly OperatorStatsService _statsService;
    private readonly IOfflineSyncHeadStore? _offlineSyncHeadStore;
    private readonly IDeterministicCombatEngine? _combatEngine;

    public OperatorService(
        OperatorExfilService exfilService,
        CombatSessionService sessionService,
        IOperatorEventStore eventStore,
        IOfflineSyncHeadStore? offlineSyncHeadStore = null,
        IDeterministicCombatEngine? combatEngine = null,
        OperatorStatsService? statsService = null)
    {
        _exfilService = exfilService;
        _sessionService = sessionService;
        _eventStore = eventStore;
        _statsService = statsService ?? new OperatorStatsService(new InMemoryOperatorStatsStore(), eventStore);
        _offlineSyncHeadStore = offlineSyncHeadStore;
        _combatEngine = combatEngine;
    }

    public async Task<ServiceResult<OperatorStateDto>> CreateOperatorAsync(OperatorCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<OperatorStateDto>.ValidationError("Operator name cannot be empty");
        }

        var result = await _exfilService.CreateOperatorAsync(request.Name, request.AccountId);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(result.Value!);
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        var stats = await _statsService.GetStatsAsync(result.Value!.Value);
        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!, stats));
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
        var stats = await _statsService.GetStatsAsync(operatorId);

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
            stats = await _statsService.GetStatsAsync(operatorId);
        }

        var operatorDto = ToDto(aggregate, stats);
        
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
                    Stats = operatorDto.Stats,
                    TotalXp = operatorDto.TotalXp,
                    CurrentHealth = operatorDto.CurrentHealth,
                    MaxHealth = operatorDto.MaxHealth,
                    EquippedWeaponName = operatorDto.EquippedWeaponName,
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
                    Stats = operatorDto.Stats,
                    TotalXp = operatorDto.TotalXp,
                    CurrentHealth = operatorDto.CurrentHealth,
                    MaxHealth = operatorDto.MaxHealth,
                    EquippedWeaponName = operatorDto.EquippedWeaponName,
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
            var clearResult = await _exfilService.ClearDanglingCombatSessionAsync(new OperatorId(operatorId));
            if (!clearResult.IsSuccess)
            {
                // Propagate the error so StartCombatSessionAsync knows the state is still inconsistent
                return clearResult;
            }
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

    public async Task<ServiceResult<List<OperatorSummaryDto>>> ListOperatorsAsync(Guid accountId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID must be non-empty.", nameof(accountId));

        var operatorIds = await _eventStore.ListOperatorIdsByAccountAsync(accountId);
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

    /// <summary>
    /// Returns the account ID associated with an operator, or null if the operator has no account.
    /// </summary>
    public async Task<Guid?> GetOperatorAccountIdAsync(Guid operatorId)
    {
        return await _eventStore.GetOperatorAccountIdAsync(new OperatorId(operatorId));
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
            Operator = ToDto(operatorAggregate, await _statsService.GetStatsAsync(operatorId))
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

        var stats = await _statsService.GetStatsAsync(operatorId);
        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!, stats));
    }

    public async Task<ServiceResult<Guid>> StartCombatSessionAsync(Guid operatorId)
    {
        var operatorKey = new OperatorId(operatorId);

        // If there is already an active combat session for this infil, reuse it instead of
        // attempting to create a second one. This keeps combat entry idempotent for web clients
        // that may retry or race against real-time updates.
        var preLoadResult = await _exfilService.LoadOperatorAsync(operatorKey);
        if (!preLoadResult.IsSuccess)
            return ServiceResult<Guid>.FromResult(preLoadResult);

        var aggregate = preLoadResult.Value!;

        if (aggregate.ActiveCombatSessionId.HasValue)
        {
            var existingSessionResult = await _sessionService.GetStateAsync(aggregate.ActiveCombatSessionId.Value);
            if (existingSessionResult.Status == ResultStatus.Success)
            {
                if (existingSessionResult.Value!.Phase != SessionPhase.Completed)
                    return ServiceResult<Guid>.Success(aggregate.ActiveCombatSessionId.Value);
            }

            // Resolve any completed or dangling session reference before attempting to start a new combat.
            var cleanupResult = await CleanupCompletedSessionAsync(operatorId);
            if (!cleanupResult.IsSuccess)
                return ServiceResult<Guid>.FromResult(cleanupResult);

            // Reload because cleanup may have processed the combat outcome or cleared a stale reference.
            var loadResult = await _exfilService.LoadOperatorAsync(operatorKey);
            if (!loadResult.IsSuccess)
                return ServiceResult<Guid>.FromResult(loadResult);

            aggregate = loadResult.Value!;
        }

        // Pre-generate the session ID so we can create the session record BEFORE emitting the
        // CombatSessionStartedEvent. This removes the race condition where operator SSE subscribers
        // receive a notification while the session store entry doesn't yet exist.
        var sessionId = Guid.NewGuid();

        // Step 1: Create the combat session in the store so it exists when SSE subscribers react.
        var sessionRequest = new SessionCreateRequest
        {
            Id = sessionId,
            OperatorId = operatorId,
            PlayerName = aggregate.Name,
            PlayerTotalXp = aggregate.TotalXp
        };
        var sessionResult = await _sessionService.CreateSessionAsync(sessionRequest);
        if (!sessionResult.IsSuccess)
            return ServiceResult<Guid>.FromResult(sessionResult);

        // Step 2: Emit CombatSessionStartedEvent on the operator aggregate (fires operator SSE).
        // The session record already exists, so any subscriber that immediately fetches operator
        // state will find a populated ActiveCombatSession.
        var exfilResult = await _exfilService.StartCombatSessionAsync(new OperatorId(operatorId), sessionId);
        if (!exfilResult.IsSuccess)
        {
            // The session record remains in the database as an audit trail; do not delete it.
            return exfilResult;
        }

        return ServiceResult<Guid>.Success(sessionId);
    }

    public async Task<ServiceResult> CompleteInfilAsync(Guid operatorId)
    {
        // Auto-process any pending completed combat session outcome before completing the infil.
        // This ensures the replay-derived combat outcome (XP, death, mode transitions) is always
        // applied even when the client does not explicitly call ProcessCombatOutcomeAsync first.
        var cleanupResult = await CleanupCompletedSessionAsync(operatorId);
        if (!cleanupResult.IsSuccess)
            return cleanupResult;

        // Reload the operator after cleanup — the outcome may have transitioned them to Base mode
        // (e.g. operator died, causing OperatorDiedEvent + InfilEndedEvent to be emitted).
        var reloadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!reloadResult.IsSuccess)
        {
            return reloadResult.Status switch
            {
                ResultStatus.NotFound => ServiceResult.NotFound(reloadResult.ErrorMessage),
                _ => ServiceResult.InvalidState(reloadResult.ErrorMessage!)
            };
        }

        // If the operator is no longer in Infil mode (e.g. they died and cleanup emitted InfilEndedEvent),
        // the infil has already been ended — return success without emitting a duplicate InfilEndedEvent.
        if (reloadResult.Value!.CurrentMode != OperatorMode.Infil)
            return ServiceResult.Success();

        return await _exfilService.CompleteInfilSuccessfullyAsync(new OperatorId(operatorId));
    }

    /// <summary>
    /// Retreats from the active combat session.
    /// Emits a <see cref="Core.Operators.CombatVictoryEvent"/> to clear <c>ActiveCombatSessionId</c>
    /// so the operator can enter a new combat or exfil, while the session record is preserved in the
    /// database for audit purposes.
    /// </summary>
    public async Task<ServiceResult> RetreatFromCombatAsync(Guid operatorId)
    {
        return await _exfilService.ClearDanglingCombatSessionAsync(new OperatorId(operatorId));
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

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!, await _statsService.GetStatsAsync(operatorId)));
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

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!, await _statsService.GetStatsAsync(operatorId)));
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

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!, await _statsService.GetStatsAsync(operatorId)));
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

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!, await _statsService.GetStatsAsync(operatorId)));
    }

    public async Task<ServiceResult> SyncOfflineMission(OfflineMissionEnvelope envelope)
    {
        if (!Guid.TryParse(envelope.OperatorId, out var operatorId))
            return ServiceResult.ValidationError("Offline mission envelope has invalid operator ID");
        if (envelope.SequenceNumber <= 0)
            return ServiceResult.ValidationError("Offline mission envelope sequence must be positive");

        if (string.IsNullOrEmpty(envelope.InitialSnapshotJson))
            return ServiceResult.ValidationError("Offline mission envelope must include an initial operator snapshot");
        if (string.IsNullOrEmpty(envelope.InitialCombatSnapshotJson))
            return ServiceResult.ValidationError("Offline mission envelope must include an initial combat snapshot");
        if (string.IsNullOrEmpty(envelope.FinalCombatSnapshotHash))
            return ServiceResult.ValidationError("Offline mission envelope must include a final combat snapshot hash");
        if (envelope.ReplayTurns == null || envelope.ReplayTurns.Count == 0)
            return ServiceResult.ValidationError("Offline mission envelope must include recorded replay turns");

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

        var currentStateResult = await GetOperatorAsync(operatorId);
        if (!currentStateResult.IsSuccess)
        {
            return currentStateResult.Status switch
            {
                ResultStatus.NotFound => ServiceResult.NotFound(currentStateResult.ErrorMessage),
                ResultStatus.InvalidState => ServiceResult.InvalidState(currentStateResult.ErrorMessage!),
                ResultStatus.ValidationError => ServiceResult.ValidationError(currentStateResult.ErrorMessage!),
                _ => ServiceResult.InvalidState(currentStateResult.ErrorMessage!)
            };
        }

        var currentState = currentStateResult.Value!;
        if (previous == null)
        {
            // First accepted envelope for this operator is anchored to current server operator state.
            // Later envelopes are anchored to the persisted sync head hash chain.
            var currentHash = OfflineMissionHashing.ComputeOperatorStateHash(currentState);
            if (!string.Equals(currentHash, envelope.InitialOperatorStateHash, StringComparison.Ordinal))
                return ServiceResult.InvalidState("Offline mission envelope initial state hash mismatch");
        }

        OperatorDto initialOperator;
        try
        {
            initialOperator = JsonSerializer.Deserialize<OperatorDto>(envelope.InitialSnapshotJson, _replayJsonOptions)
                ?? throw new InvalidOperationException();
        }
        catch (Exception)
        {
            return ServiceResult.InvalidState("Offline mission envelope initial operator snapshot could not be deserialized");
        }

        // Validate the operator identity separately from the snapshot hash so malformed or
        // misrouted envelopes fail with a precise, stable error before replay/apply work begins.
        if (!Guid.TryParse(initialOperator.Id, out var initialOperatorId))
            return ServiceResult.InvalidState("Offline mission envelope initial operator ID is invalid");

        if (initialOperatorId != currentState.Id)
            return ServiceResult.InvalidState("Offline mission envelope initial operator ID mismatch");

        var initialSnapshotHash = OfflineMissionHashing.ComputeOperatorStateHash(initialOperator);
        if (!string.Equals(initialSnapshotHash, envelope.InitialOperatorStateHash, StringComparison.Ordinal))
            return ServiceResult.InvalidState("Offline mission envelope initial snapshot hash mismatch");

        OfflineCombatReplayResult replay;
        try
        {
            replay = await OfflineCombatReplay.ReplayAsync(envelope.InitialCombatSnapshotJson, envelope.ReplayTurns);
        }
        catch (Exception)
        {
            return ServiceResult.InvalidState("Offline mission replay failed");
        }

        var replayedCombatHash = OfflineCombatReplay.ComputeCombatSnapshotHash(replay.FinalSession);
        if (!string.Equals(replayedCombatHash, envelope.FinalCombatSnapshotHash, StringComparison.Ordinal))
            return ServiceResult.InvalidState("Offline mission envelope combat replay hash mismatch");

        if (!BattleLogsMatch(replay.FinalSession.BattleLog, envelope.FullBattleLog))
            return ServiceResult.InvalidState("Offline mission envelope battle log mismatch");

        var replayedState = OfflineCombatReplay.ProjectOperatorResult(initialOperator, replay.Outcome);
        var replayedHash = OfflineMissionHashing.ComputeOperatorStateHash(replayedState);
        if (!string.Equals(replayedHash, envelope.ResultOperatorStateHash, StringComparison.Ordinal))
            return ServiceResult.InvalidState("Offline mission envelope final state hash mismatch");

        var applyResult = await _exfilService.ProcessCombatOutcomeAsync(replay.Outcome, playerConfirmed: true);
        if (applyResult.Status != ResultStatus.Success)
        {
            return applyResult.Status switch
            {
                ResultStatus.NotFound => ServiceResult.NotFound(applyResult.ErrorMessage),
                ResultStatus.ValidationError => ServiceResult.ValidationError(applyResult.ErrorMessage!),
                _ => ServiceResult.InvalidState(applyResult.ErrorMessage!)
            };
        }

        var postSyncState = await GetOperatorAsync(operatorId);
        if (!postSyncState.IsSuccess || postSyncState.Value == null)
            return ServiceResult.InvalidState(postSyncState.ErrorMessage ?? "Offline mission sync could not reload operator state.");

        var actualResultHash = OfflineMissionHashing.ComputeOperatorStateHash(postSyncState.Value);
        if (!string.Equals(actualResultHash, envelope.ResultOperatorStateHash, StringComparison.Ordinal))
            return ServiceResult.InvalidState("Offline mission envelope applied state hash mismatch");

        if (_offlineSyncHeadStore != null)
        {
            await _offlineSyncHeadStore.UpsertAsync(new OfflineSyncHead
            {
                OperatorId = operatorId,
                SequenceNumber = envelope.SequenceNumber,
                ResultOperatorStateHash = actualResultHash
            });
        }
        return ServiceResult.Success();
    }

    private static readonly JsonSerializerOptions _replayJsonOptions = new(JsonSerializerDefaults.Web);

    private static bool BattleLogsMatch(
        IReadOnlyList<BattleLogEntryDto> actual,
        IReadOnlyList<BattleLogEntryDto>? expected)
    {
        expected ??= [];
        if (actual.Count != expected.Count)
            return false;

        for (var i = 0; i < actual.Count; i++)
        {
            if (!string.Equals(actual[i].EventType, expected[i].EventType, StringComparison.Ordinal) ||
                actual[i].TimeMs != expected[i].TimeMs ||
                !string.Equals(actual[i].Message, expected[i].Message, StringComparison.Ordinal) ||
                !string.Equals(actual[i].ActorName, expected[i].ActorName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static OperatorStateDto ToDto(OperatorAggregate aggregate, OperatorStats stats)
    {
        return new OperatorStateDto
        {
            Id = aggregate.Id.Value,
            Name = aggregate.Name,
            Stats = new OperatorStatsDto
            {
                OperatorId = stats.OperatorId,
                InfilCount = stats.InfilCount,
                ExfilCount = stats.ExfilCount,
                TotalInfilDurationTicks = stats.TotalInfilDurationTicks,
                EnemyKills = stats.EnemyKills
            },
            TotalXp = aggregate.TotalXp,
            CurrentHealth = aggregate.CurrentHealth,
            MaxHealth = aggregate.MaxHealth,
            EquippedWeaponName = aggregate.EquippedWeaponName,
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
