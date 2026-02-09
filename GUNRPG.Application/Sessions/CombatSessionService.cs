using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Mapping;
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

    public CombatSessionService(ICombatSessionStore store)
    {
        _store = store;
    }

    public async Task<CombatSessionDto> CreateSessionAsync(SessionCreateRequest request)
    {
        var session = CombatSession.CreateDefault(
            playerName: request.PlayerName,
            seed: request.Seed,
            startingDistance: request.StartingDistance,
            enemyName: request.EnemyName);

        await _store.SaveAsync(SessionMapping.ToSnapshot(session));
        return SessionMapping.ToDto(session);
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
}
