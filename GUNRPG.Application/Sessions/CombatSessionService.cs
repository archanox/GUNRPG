using GUNRPG.Application.Combat;
using GUNRPG.Application.Distributed;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// Application service that orchestrates combat sessions and exposes UI-agnostic operations.
/// When an <see cref="IGameAuthority"/> is provided, combat actions are replicated
/// through the distributed lockstep authority for P2P state verification.
/// </summary>
public sealed class CombatSessionService
{
    private const float VictoryDifficultyModifier = 0.9f;
    private const float DefeatDifficultyModifier = 1.2f;
    private const int XpMultiplier = 20;

    private readonly ICombatSessionStore _store;
    private readonly IOperatorEventStore? _operatorEventStore;
    private readonly IGameAuthority? _gameAuthority;
    private readonly CombatSessionUpdateHub? _updateHub;

    public CombatSessionService(ICombatSessionStore store, IOperatorEventStore? operatorEventStore = null, IGameAuthority? gameAuthority = null, CombatSessionUpdateHub? updateHub = null)
    {
        _store = store;
        _operatorEventStore = operatorEventStore;
        _gameAuthority = gameAuthority;
        _updateHub = updateHub;
    }

    public async Task<ServiceResult<CombatSessionDto>> CreateSessionAsync(SessionCreateRequest request)
    {
        if (request.OperatorId.HasValue && request.OperatorId.Value == Guid.Empty)
        {
            return ServiceResult<CombatSessionDto>.ValidationError("Operator ID cannot be empty");
        }

        if (request.Id.HasValue)
        {
            if (request.Id.Value == Guid.Empty)
            {
                return ServiceResult<CombatSessionDto>.ValidationError("Session ID cannot be empty");
            }

            var existing = await _store.LoadAsync(request.Id.Value);
            if (existing != null)
            {
                return ServiceResult<CombatSessionDto>.InvalidState("A session with the provided ID already exists");
            }
        }

        var session = CombatSession.CreateDefault(
            playerName: request.PlayerName,
            seed: request.Seed,
            startingDistance: request.StartingDistance,
            enemyName: request.EnemyName,
            id: request.Id,
            operatorId: request.OperatorId,
            playerTotalXp: request.PlayerTotalXp);

        if (string.IsNullOrEmpty(session.ReplayInitialSnapshotJson))
        {
            session.SetReplayInitialSnapshotJson(OfflineCombatReplay.SerializeCombatSnapshot(SessionMapping.ToSnapshot(session)));
        }

        await _store.SaveAsync(SessionMapping.ToSnapshot(session));
        return ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(session));
    }

    public async Task<ServiceResult<CombatSessionDto>> GetStateAsync(Guid sessionId)
    {
        var session = await LoadAsync(sessionId);
        return session == null
            ? ServiceResult<CombatSessionDto>.NotFound("Session not found")
            : ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(session));
    }

    /// <summary>
    /// Submits player intents and derives the new session state via full replay of the authoritative
    /// replay log: <c>state = replay(ReplayTurns + [newIntent], Seed)</c>.
    /// The stored snapshot is a cache of the replay result — no state is mutated outside this path.
    /// For sessions without a recorded initial snapshot (legacy), the turn is applied incrementally
    /// as a fallback.
    /// </summary>
    public async Task<ServiceResult<CombatSessionDto>> SubmitPlayerIntentsAsync(Guid sessionId, SubmitIntentsRequest request)
    {
        // 1. Load the current snapshot for validation and replay data.
        var snapshot = await _store.LoadAsync(sessionId);
        if (snapshot == null)
        {
            return ServiceResult<CombatSessionDto>.NotFound("Session not found");
        }

        // 2. Reconstruct session from snapshot for validation only.
        var validationSession = SessionMapping.FromSnapshot(snapshot);

        // 3. Validate operator is in Infil mode and session matches the active session.
        var modeValidation = await ValidateOperatorInInfilModeAsync(validationSession, request.OperatorId);
        if (modeValidation != null)
        {
            return ServiceResult<CombatSessionDto>.FromResult(modeValidation);
        }

        // 4. Validate session phase and combat state.
        if (validationSession.Phase != SessionPhase.Planning)
        {
            return ServiceResult<CombatSessionDto>.InvalidState("Intents can only be submitted during the Planning phase");
        }

        if (validationSession.Combat.Phase == CombatPhase.Ended)
        {
            return ServiceResult<CombatSessionDto>.InvalidState("Combat has already ended");
        }

        // 5. Validate the intent against the current cached state.
        var playerIntents = SessionMapping.ToDomainIntent(validationSession.Player.Id, request.Intents);
        var submission = validationSession.Combat.SubmitIntents(validationSession.Player, playerIntents);
        if (!submission.success)
        {
            return ServiceResult<CombatSessionDto>.ValidationError(submission.errorMessage ?? "Intent submission failed");
        }

        // 6. Build the IntentSnapshot that represents this new turn in the replay log.
        var newTurn = new IntentSnapshot
        {
            OperatorId = validationSession.Player.Id,
            Primary = playerIntents.Primary,
            Movement = playerIntents.Movement,
            Stance = playerIntents.Stance,
            Cover = playerIntents.Cover,
            CancelMovement = playerIntents.CancelMovement
        };

        // 7. Full replay: rebuild session state from the immutable initial snapshot + all
        //    prior turns + the new turn.  State is never mutated directly; the snapshot cached
        //    in the store is always the deterministic result of replay(ReplayTurns, Seed).
        CombatSession replaySession;
        CombatReplaySideEffectPlan sideEffectPlan = CombatReplaySideEffectPlan.None;
        if (!string.IsNullOrEmpty(snapshot.ReplayInitialSnapshotJson))
        {
            var simulationState = ReplayRunner.Run(
                snapshot.ReplayInitialSnapshotJson,
                snapshot.ReplayTurns.Append(newTurn).ToArray());
            replaySession = simulationState.Session;
            sideEffectPlan = simulationState.SideEffects;

            if (replaySession.Phase == SessionPhase.Completed)
            {
                try
                {
                    await replaySession.FinalizeAsync();
                }
                catch (Exception ex)
                {
                    return ServiceResult<CombatSessionDto>.InvalidState("Session finalization failed: " + ex.Message);
                }
            }
        }
        else
        {
            // Legacy fallback for sessions that pre-date the replay system and have no initial snapshot.
            // All sessions created via CreateSessionAsync carry a ReplayInitialSnapshotJson, so this
            // path is only exercised for test fixtures that use CombatSession.CreateDefault() directly.
            replaySession = validationSession;
            // Player intents were already submitted on validationSession in step 5; submit enemy now.
            var enemyIntents = replaySession.Ai.DecideIntents(replaySession.Enemy, replaySession.Player, replaySession.Combat);
            var enemySub = replaySession.Combat.SubmitIntents(replaySession.Enemy, enemyIntents);
            if (!enemySub.success)
                replaySession.Combat.SubmitIntents(replaySession.Enemy, SimultaneousIntents.CreateStop(replaySession.Enemy.Id));

            replaySession.RecordReplayTurn(playerIntents);
            replaySession.TransitionTo(SessionPhase.Resolving, GetReplayTimestamp(replaySession));
            replaySession.Combat.BeginExecution();
            ResolveUntilPlanningOrEnd(replaySession);
            sideEffectPlan = BuildSideEffectPlan(replaySession);

            if (replaySession.Combat.Phase == CombatPhase.Ended)
            {
                replaySession.TransitionTo(SessionPhase.Completed, GetReplayTimestamp(replaySession));
                try
                {
                    await replaySession.FinalizeAsync();
                }
                catch (Exception ex)
                {
                    return ServiceResult<CombatSessionDto>.InvalidState("Session finalization failed: " + ex.Message);
                }
            }
            else
            {
                var replayTimestamp = GetReplayTimestamp(replaySession);
                replaySession.TransitionTo(SessionPhase.Planning, replayTimestamp);
                replaySession.AdvanceTurnCounter(replayTimestamp);
            }
        }

        // 8. Persist the replay-derived state.
        replaySession.RecordAction(GetReplayTimestamp(replaySession));
        await SaveAsync(replaySession);

        // 9. Apply side effects only after the replay-derived snapshot has been persisted.
        if (sideEffectPlan.RequiresApplication)
        {
            ApplySideEffects(replaySession, sideEffectPlan);
            await SaveAsync(replaySession);
        }

        // 10. Notify the distributed authority for P2P state verification.
        if (_gameAuthority != null && !replaySession.OperatorId.IsEmpty)
        {
            await _gameAuthority.SubmitActionAsync(new PlayerActionDto
            {
                SessionId = replaySession.Id,
                OperatorId = replaySession.OperatorId.Value,
                Primary = request.Intents.Primary,
                Movement = request.Intents.Movement,
                Stance = request.Intents.Stance,
                Cover = request.Intents.Cover,
                CancelMovement = request.Intents.CancelMovement,
                ReplayInitialSnapshotJson = replaySession.ReplayInitialSnapshotJson,
                ReplayTurns = replaySession.ReplayTurns.ToList()
            });
        }

        return ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(replaySession));
    }

    /// <summary>
    /// Returns the current session state.
    /// Combat resolution is now handled atomically by <see cref="SubmitPlayerIntentsAsync"/>.
    /// This method is kept for backwards compatibility only; the <c>callerOperatorId</c> parameter
    /// is accepted but intentionally ignored — no simulation or validation is performed.
    /// </summary>
    public async Task<ServiceResult<CombatSessionDto>> AdvanceAsync(Guid sessionId, Guid? callerOperatorId = null)
    {
        return await GetStateAsync(sessionId);
    }

    /// <summary>
    /// Applies a pet input to the session. Pet state updates are side-channel mutations tracked
    /// independently of the combat replay log. They do not affect <see cref="CombatSession.FinalHash"/>
    /// or the deterministic simulation output; they represent companion welfare over real time
    /// and are not required for combat replay integrity.
    /// </summary>
    public async Task<ServiceResult<PetStateDto>> ApplyPetInputAsync(Guid sessionId, PetInput input, DateTimeOffset now)
    {
        var session = await LoadAsync(sessionId);
        if (session == null)
        {
            return ServiceResult<PetStateDto>.NotFound("Session not found");
        }

        session.PetState = PetRules.Apply(session.PetState, input, now);
        session.Player.Fatigue = session.PetState.Fatigue;
        session.RecordAction();
        await SaveAsync(session);
        return ServiceResult<PetStateDto>.Success(SessionMapping.ToDto(session.PetState));
    }

    public async Task<ServiceResult<PetStateDto>> ApplyPetActionAsync(Guid sessionId, PetActionRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var input = ResolvePetInput(request);
        return await ApplyPetInputAsync(sessionId, input, now);
    }

    private async Task<CombatSession?> LoadAsync(Guid id)
    {
        var snapshot = await _store.LoadAsync(id);
        if (snapshot == null) return null;

        return SessionMapping.FromSnapshot(snapshot);
    }

    private async Task SaveAsync(CombatSession session)
    {
        await _store.SaveAsync(SessionMapping.ToSnapshot(session));
        _updateHub?.Publish(session.Id);
    }

    /// <summary>
    /// Validates that the operator associated with a session is in Infil mode.
    /// Returns null if validation passes, or an error result if it fails.
    /// </summary>
    private async Task<ServiceResult?> ValidateOperatorInInfilModeAsync(CombatSession session, Guid? callerOperatorId = null)
    {
        // If operator event store is not available, skip validation (e.g., in-memory mode or tests)
        if (_operatorEventStore == null)
        {
            return null;
        }

        // If session has no operator ID, skip validation (legacy sessions or test data)
        if (session.OperatorId.IsEmpty)
        {
            return null;
        }

        // Validate the caller operator ID matches the session's owning operator (prevents cross-operator tamper)
        if (callerOperatorId.HasValue && callerOperatorId.Value != session.OperatorId.Value)
        {
            return ServiceResult.InvalidState("Session does not belong to the specified operator");
        }

        try
        {
            var events = await _operatorEventStore.LoadEventsAsync(session.OperatorId);
            if (events.Count == 0)
            {
                return ServiceResult.NotFound("Operator not found");
            }

            var aggregate = OperatorAggregate.FromEvents(events);

            if (aggregate.CurrentMode != OperatorMode.Infil)
            {
                return ServiceResult.InvalidState("Combat actions are only allowed when operator is in Infil mode");
            }

            if (aggregate.ActiveCombatSessionId == null || aggregate.ActiveCombatSessionId.Value != session.Id)
            {
                return ServiceResult.InvalidState("Session does not match the operator's active combat session");
            }

            return null;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("hash") || ex.Message.Contains("chain") || ex.Message.Contains("corrupted"))
        {
            // Event stream corruption - fail closed (reject action)
            return ServiceResult.InvalidState($"Operator data corrupted: {ex.Message}");
        }
        catch (Exception ex)
        {
            // For any other unexpected error, fail closed (reject action) to ensure security
            return ServiceResult.InvalidState($"Failed to validate operator mode: {ex.Message}");
        }
    }

    /// <summary>
    /// Executes one combat turn directly on a <see cref="CombatSession"/> domain object, without
    /// any service-layer concerns. Used by both the replay loop inside
    /// <see cref="SubmitPlayerIntentsAsync"/> and by <see cref="OfflineCombatReplay.ReplayAsync"/>.
    /// <para>
    /// <b>Accessibility:</b> <c>internal</c> (not <c>private</c>) so that
    /// <see cref="OfflineCombatReplay"/>, which lives in a sibling namespace within the same
    /// assembly, can call it directly without going through the public service API.
    /// Callers outside <c>GUNRPG.Application</c> must use <see cref="SubmitPlayerIntentsAsync"/>
    /// or <see cref="OfflineCombatReplay.ReplayAsync"/> instead.
    /// </para>
    /// <para>
    /// <b>FinalizeAsync is intentionally NOT called here.</b>  The caller is responsible for
    /// invoking <see cref="CombatSession.FinalizeAsync"/> after the full replay loop completes,
    /// which upgrades the interim input-based <c>FinalHash</c> to the canonical
    /// <c>hash(replay(ReplayTurns, Seed))</c>.  Calling it here would create a recursive chain:
    /// <c>FinalizeAsync → OfflineCombatReplay.ReplayAsync → ExecuteReplayTurn → FinalizeAsync</c>.
    /// </para>
    /// </summary>
    internal static void ExecuteReplayTurn(CombatSession session, IntentSnapshot turn)
    {
        if (session.Phase == SessionPhase.Completed)
            return;

        // Player intents are keyed to the session player; enemy intents are AI-generated and
        // are not part of the replay record.
        var playerIntents = new SimultaneousIntents(session.Player.Id)
        {
            Primary = turn.Primary,
            Movement = turn.Movement,
            Stance = turn.Stance,
            Cover = turn.Cover,
            CancelMovement = turn.CancelMovement,
            SubmittedAtMs = turn.SubmittedAtMs
        };

        var submission = session.Combat.SubmitIntents(session.Player, playerIntents);
        if (!submission.success)
            throw new InvalidOperationException(
                $"Replay turn ({turn.Primary}) intent submission failed: {submission.errorMessage}");

        var enemyIntents = session.Ai.DecideIntents(session.Enemy, session.Player, session.Combat);
        var enemySubmission = session.Combat.SubmitIntents(session.Enemy, enemyIntents);
        if (!enemySubmission.success)
            session.Combat.SubmitIntents(session.Enemy, SimultaneousIntents.CreateStop(session.Enemy.Id));

        // Append the player intent to ReplayTurns before transitioning (RecordReplayTurn throws
        // when Phase == Completed).
        session.RecordReplayTurn(playerIntents);
        var resolveTransitionTimestamp = GetReplayTimestamp(session);
        session.TransitionTo(SessionPhase.Resolving, resolveTransitionTimestamp);
        session.Combat.BeginExecution();
        ResolveUntilPlanningOrEnd(session);

        var replayTimestamp = GetReplayTimestamp(session);
        if (session.Combat.Phase == CombatPhase.Ended)
        {
            // Sets the interim input-based FinalHash; caller must invoke FinalizeAsync to upgrade
            // it to the replay-based hash.
            session.TransitionTo(SessionPhase.Completed, replayTimestamp);
        }
        else
        {
            session.TransitionTo(SessionPhase.Planning, replayTimestamp);
            session.AdvanceTurnCounter(replayTimestamp);
        }

        session.RecordAction(replayTimestamp);
    }

    private static void ResolveUntilPlanningOrEnd(CombatSession session)
    {
        if (session.Combat.Phase == CombatPhase.Planning)
        {
            return;
        }

        while (session.Combat.Phase == CombatPhase.Executing)
        {
            var hasMoreEvents = session.Combat.ExecuteUntilReactionWindow();
            if (!hasMoreEvents || session.Combat.Phase != CombatPhase.Executing)
            {
                break;
            }
        }
    }

    internal static CombatReplaySideEffectPlan BuildSideEffectPlan(CombatSession session)
    {
        if (session.PostCombatResolved || session.Combat.Phase != CombatPhase.Ended)
        {
            return CombatReplaySideEffectPlan.None;
        }

        float healthLost = session.Player.MaxHealth - session.Player.Health;
        int hitsTaken = (int)Math.Ceiling(healthLost / 10f);

        // TODO: Player level is no longer stored in session (removed as part of operator/combat separation).
        // Options to fix difficulty calculation:
        // 1. Load operator aggregate via session.OperatorId to get actual level (adds dependency)
        // 2. Pass level from caller (requires API change)
        // 3. Refactor OpponentDifficulty.Compute to not require player level
        // PlayerLevel is now stored in the session at creation time via SessionCreateRequest.PlayerTotalXp
        float opponentDifficulty = OpponentDifficulty.Compute(
            opponentLevel: session.EnemyLevel,
            playerLevel: session.PlayerLevel);

        if (session.Player.IsAlive && !session.Enemy.IsAlive)
        {
            opponentDifficulty *= VictoryDifficultyModifier;
        }
        else if (!session.Player.IsAlive)
        {
            opponentDifficulty = Math.Min(100f, opponentDifficulty * DefeatDifficultyModifier);
        }

        var missionResolvedAt = GetReplayTimestamp(session);

        return new CombatReplaySideEffectPlan(
            RequiresApplication: true,
            PlayerSurvived: session.Player.IsAlive,
            PetMissionInput: new MissionInput(hitsTaken, opponentDifficulty),
            MissionResolvedAt: missionResolvedAt);
    }

    private static void ApplySideEffects(CombatSession session, CombatReplaySideEffectPlan plan)
    {
        if (!plan.RequiresApplication || session.PostCombatResolved)
        {
            return;
        }

        session.OperatorManager.CompleteCombat(session.Player, plan.PlayerSurvived);
        if (plan.PetMissionInput != null && plan.MissionResolvedAt.HasValue)
        {
            session.PetState = PetRules.Apply(session.PetState, plan.PetMissionInput, plan.MissionResolvedAt.Value);
            session.Player.Fatigue = session.PetState.Fatigue;
        }

        // XP gain is now handled by OperatorExfilService, not in combat sessions
        session.PostCombatResolved = true;
    }

    private static PetInput ResolvePetInput(PetActionRequest request)
    {
        var action = request.Action?.Trim().ToLowerInvariant() ?? "rest";
        return action switch
        {
            "eat" => new EatInput(request.Nutrition ?? 20f),
            "drink" => new DrinkInput(request.Hydration ?? 20f),
            "mission" => new MissionInput(request.HitsTaken ?? 0, request.OpponentDifficulty ?? 50f),
            _ => new RestInput(TimeSpan.FromHours(request.Hours ?? 1f))
        };
    }

    internal static DateTimeOffset GetReplayTimestamp(CombatSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.CreatedAt + CombatSession.ToBoundedCombatDuration(session.Combat.CurrentTimeMs);
    }
    
    /// <summary>
    /// Gets the combat outcome for a completed session.
    /// </summary>
    public async Task<ServiceResult<CombatOutcome>> GetCombatOutcomeAsync(Guid sessionId)
    {
        var session = await LoadAsync(sessionId);
        if (session == null)
        {
            return ServiceResult<CombatOutcome>.NotFound("Session not found");
        }

        if (session.Phase != SessionPhase.Completed)
        {
            return ServiceResult<CombatOutcome>.InvalidState("Combat session is not completed yet");
        }

        try
        {
            var outcome = session.GetOutcome();
            return ServiceResult<CombatOutcome>.Success(outcome);
        }
        catch (Exception ex)
        {
            return ServiceResult<CombatOutcome>.InvalidState($"Failed to get outcome: {ex.Message}");
        }
    }

    /// <summary>
    /// Combat sessions are retained as audit records and cannot be deleted.
    /// </summary>
    public async Task<ServiceResult> DeleteSessionAsync(Guid sessionId)
    {
        var existing = await LoadAsync(sessionId);
        if (existing == null)
            return ServiceResult.NotFound("Session not found");

        return ServiceResult.InvalidState("Combat sessions are audit records and cannot be deleted.");
    }
}
