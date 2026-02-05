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

    public CombatSessionDto CreateSession(SessionCreateRequest request)
    {
        var session = CombatSession.CreateDefault(
            playerName: request.PlayerName,
            seed: request.Seed,
            startingDistance: request.StartingDistance,
            enemyName: request.EnemyName);

        _store.Create(session);
        return SessionMapping.ToDto(session);
    }

    public ServiceResult<CombatSessionDto> GetState(Guid sessionId)
    {
        var session = _store.Get(sessionId);
        return session == null
            ? ServiceResult<CombatSessionDto>.NotFound("Session not found")
            : ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(session));
    }

    /// <summary>
    /// Submits player intents without advancing the turn. This only records the intent.
    /// Call Advance() separately to resolve the turn.
    /// </summary>
    public IntentSubmissionResultDto SubmitPlayerIntents(Guid sessionId, SubmitIntentsRequest request)
    {
        var session = _store.Get(sessionId);
        if (session == null)
        {
            return new IntentSubmissionResultDto
            {
                Accepted = false,
                Error = "Session not found"
            };
        }

        if (session.Combat.Phase == CombatPhase.Ended)
        {
            return new IntentSubmissionResultDto
            {
                Accepted = false,
                Error = "Combat has already ended",
                State = SessionMapping.ToDto(session)
            };
        }

        var playerIntents = SessionMapping.ToDomainIntent(session.Player.Id, request.Intents);
        var submission = session.Combat.SubmitIntents(session.Player, playerIntents);
        if (!submission.success)
        {
            return new IntentSubmissionResultDto
            {
                Accepted = false,
                Error = submission.errorMessage,
                State = SessionMapping.ToDto(session)
            };
        }

        var enemyIntents = session.Ai.DecideIntents(session.Enemy, session.Player, session.Combat);
        var enemySubmission = session.Combat.SubmitIntents(session.Enemy, enemyIntents);
        if (!enemySubmission.success)
        {
            session.Combat.SubmitIntents(session.Enemy, SimultaneousIntents.CreateStop(session.Enemy.Id));
        }

        if (session.Combat.Phase == CombatPhase.Planning)
        {
            session.Combat.BeginExecution();
        }

        _store.Upsert(session);
        return new IntentSubmissionResultDto
        {
            Accepted = true,
            State = SessionMapping.ToDto(session)
        };
    }

    /// <summary>
    /// Advances the combat turn until the next planning phase or end of combat.
    /// </summary>
    public ServiceResult<CombatSessionDto> Advance(Guid sessionId)
    {
        var session = _store.Get(sessionId);
        if (session == null)
        {
            return ServiceResult<CombatSessionDto>.NotFound("Session not found");
        }

        if (session.Combat.Phase == CombatPhase.Executing)
        {
            ResolveUntilPlanningOrEnd(session);
        }

        if (session.Combat.Phase == CombatPhase.Ended)
        {
            ApplyPostCombat(session);
        }

        _store.Upsert(session);
        return ServiceResult<CombatSessionDto>.Success(SessionMapping.ToDto(session));
    }

    public ServiceResult<PetStateDto> ApplyPetInput(Guid sessionId, PetInput input, DateTimeOffset now)
    {
        var session = _store.Get(sessionId);
        if (session == null)
        {
            return ServiceResult<PetStateDto>.NotFound("Session not found");
        }

        session.PetState = PetRules.Apply(session.PetState, input, now);
        session.Player.Fatigue = session.PetState.Fatigue;
        _store.Upsert(session);
        return ServiceResult<PetStateDto>.Success(SessionMapping.ToDto(session.PetState));
    }

    public ServiceResult<PetStateDto> ApplyPetAction(Guid sessionId, PetActionRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        var input = ResolvePetInput(request);
        return ApplyPetInput(sessionId, input, now);
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

        session.PetState = PetRules.Apply(session.PetState, new MissionInput(hitsTaken, opponentDifficulty), DateTimeOffset.UtcNow);

        if (session.Player.IsAlive && !session.Enemy.IsAlive)
        {
            long xpGained = (long)(opponentDifficulty * XpMultiplier);
            session.PlayerXp += xpGained;
            session.PlayerLevel = OpponentDifficulty.ComputeLevelFromXp(session.PlayerXp);
        }

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
}
