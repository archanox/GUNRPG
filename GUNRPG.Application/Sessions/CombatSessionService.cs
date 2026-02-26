using GUNRPG.Application.Combat;
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
/// </summary>
public sealed class CombatSessionService
{
    private const float VictoryDifficultyModifier = 0.9f;
    private const float DefeatDifficultyModifier = 1.2f;
    private const int XpMultiplier = 20;

    private readonly ICombatSessionStore _store;
    private readonly IOperatorEventStore? _operatorEventStore;

    public CombatSessionService(ICombatSessionStore store, IOperatorEventStore? operatorEventStore = null)
    {
        _store = store;
        _operatorEventStore = operatorEventStore;
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
            operatorId: request.OperatorId);

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
    /// Submits player intents without advancing the turn. This only records the intent.
    /// Call Advance() separately to resolve the turn.
    /// </summary>
    public async Task<ServiceResult<CombatSessionDto>> SubmitPlayerIntentsAsync(Guid sessionId, SubmitIntentsRequest request)
    {
        var session = await LoadAsync(sessionId);
        if (session == null)
        {
            return ServiceResult<CombatSessionDto>.NotFound("Session not found");
        }

        // Validate operator is in Infil mode before allowing combat actions
        var modeValidation = await ValidateOperatorInInfilModeAsync(session);
        if (modeValidation != null)
        {
            return ServiceResult<CombatSessionDto>.FromResult(modeValidation);
        }

        if (session.Phase != SessionPhase.Planning)
        {
            return ServiceResult<CombatSessionDto>.InvalidState("Intents can only be submitted during the Planning phase");
        }

        if (session.Combat.Phase == CombatPhase.Ended)
        {
            return ServiceResult<CombatSessionDto>.InvalidState("Combat has already ended");
        }

        var playerIntents = SessionMapping.ToDomainIntent(session.Player.Id, request.Intents);
        var submission = session.Combat.SubmitIntents(session.Player, playerIntents);
        if (!submission.success)
        {
            return ServiceResult<CombatSessionDto>.ValidationError(submission.errorMessage ?? "Intent submission failed");
        }

        var enemyIntents = session.Ai.DecideIntents(session.Enemy, session.Player, session.Combat);
        var enemySubmission = session.Combat.SubmitIntents(session.Enemy, enemyIntents);
        if (!enemySubmission.success)
        {
            session.Combat.SubmitIntents(session.Enemy, SimultaneousIntents.CreateStop(session.Enemy.Id));
        }

        session.RecordAction();
        await SaveAsync(session);
        return ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(session));
    }

    /// <summary>
    /// Advances the combat turn until the next planning phase or end of combat.
    /// </summary>
    public async Task<ServiceResult<CombatSessionDto>> AdvanceAsync(Guid sessionId)
    {
        var session = await LoadAsync(sessionId);
        if (session == null)
        {
            return ServiceResult<CombatSessionDto>.NotFound("Session not found");
        }

        // Validate operator is in Infil mode before allowing combat actions
        var modeValidation = await ValidateOperatorInInfilModeAsync(session);
        if (modeValidation != null)
        {
            return ServiceResult<CombatSessionDto>.FromResult(modeValidation);
        }

        if (session.Phase != SessionPhase.Planning && session.Phase != SessionPhase.Resolving)
        {
            return ServiceResult<CombatSessionDto>.InvalidState("Advance is only allowed during Planning or Resolving phases");
        }

        if (session.Combat.Phase == CombatPhase.Ended)
        {
            ApplyPostCombat(session);
            session.TransitionTo(SessionPhase.Completed);
            await SaveAsync(session);
            return ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(session));
        }

        var pendingIntents = session.Combat.GetPendingIntents();
        var hasPlayerIntents = pendingIntents.player != null;
        var hasEnemyIntents = pendingIntents.enemy != null;
        if (!hasPlayerIntents || !hasEnemyIntents)
        {
            return ServiceResult<CombatSessionDto>.InvalidState("Advance requires recorded intents for both sides");
        }

        if (session.Combat.Phase == CombatPhase.Planning)
        {
            session.TransitionTo(SessionPhase.Resolving);
            session.Combat.BeginExecution();
        }

        if (session.Combat.Phase == CombatPhase.Executing)
        {
            ResolveUntilPlanningOrEnd(session);
        }

        if (session.Combat.Phase == CombatPhase.Ended)
        {
            ApplyPostCombat(session);
            session.TransitionTo(SessionPhase.Completed);
        }
        else
        {
            session.TransitionTo(SessionPhase.Planning);
            session.AdvanceTurnCounter();
        }

        session.RecordAction();
        await SaveAsync(session);
        return ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(session));
    }

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
        return snapshot == null ? null : SessionMapping.FromSnapshot(snapshot);
    }

    private async Task SaveAsync(CombatSession session)
    {
        await _store.SaveAsync(SessionMapping.ToSnapshot(session));
    }

    /// <summary>
    /// Validates that the operator associated with a session is in Infil mode.
    /// Returns null if validation passes, or an error result if it fails.
    /// </summary>
    private async Task<ServiceResult?> ValidateOperatorInInfilModeAsync(CombatSession session)
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

        if (session.Combat.Phase == CombatPhase.Ended)
        {
            ApplyPostCombat(session);
        }
    }

    private static void ApplyPostCombat(CombatSession session)
    {
        if (session.PostCombatResolved)
        {
            return;
        }

        session.OperatorManager.CompleteCombat(session.Player, session.Player.IsAlive);

        float healthLost = session.Player.MaxHealth - session.Player.Health;
        int hitsTaken = (int)Math.Ceiling(healthLost / 10f);

        // TODO: Player level is no longer stored in session (removed as part of operator/combat separation).
        // Options to fix difficulty calculation:
        // 1. Load operator aggregate via session.OperatorId to get actual level (adds dependency)
        // 2. Pass level from caller (requires API change)
        // 3. Refactor OpponentDifficulty.Compute to not require player level
        // For now, using 0 as a placeholder - this will treat all operators as level 0 for pet/mission calculations
        float opponentDifficulty = OpponentDifficulty.Compute(
            opponentLevel: session.EnemyLevel,
            playerLevel: 0);  // Placeholder - see TODO above

        if (session.Player.IsAlive && !session.Enemy.IsAlive)
        {
            opponentDifficulty *= VictoryDifficultyModifier;
        }
        else if (!session.Player.IsAlive)
        {
            opponentDifficulty = Math.Min(100f, opponentDifficulty * DefeatDifficultyModifier);
        }

        session.PetState = PetRules.Apply(session.PetState, new MissionInput(hitsTaken, opponentDifficulty), DateTimeOffset.UtcNow);

        // XP gain is now handled by OperatorExfilService, not in combat sessions

        session.Player.Fatigue = session.PetState.Fatigue;
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
    /// Deletes a combat session. This is used when retreating from combat.
    /// </summary>
    public async Task<ServiceResult> DeleteSessionAsync(Guid sessionId)
    {
        await _store.DeleteAsync(sessionId);
        return ServiceResult.Success();
    }
}
