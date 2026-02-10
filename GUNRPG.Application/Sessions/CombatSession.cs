using GUNRPG.Application.Combat;
using GUNRPG.Core;
using GUNRPG.Core.AI;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Equipment;
using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// Aggregates combat state, AI, and operator lifecycle for a single session.
/// </summary>
public sealed class CombatSession
{
    public Guid Id { get; }
    public OperatorId OperatorId { get; }  // Reference to the operator (does not store progression)
    public CombatSystemV2 Combat { get; }
    public SimpleAIV2 Ai { get; }
    public OperatorManager OperatorManager { get; }
    public PetState PetState { get; set; }
    public int EnemyLevel { get; }
    public int Seed { get; }
    public SessionPhase Phase { get; private set; }
    public int TurnNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public bool PostCombatResolved { get; set; }

    public Operator Player => Combat.Player;
    public Operator Enemy => Combat.Enemy;

    public CombatSession(
        Guid id,
        OperatorId operatorId,
        CombatSystemV2 combat,
        SimpleAIV2 ai,
        OperatorManager operatorManager,
        PetState petState,
        int enemyLevel,
        int seed,
        SessionPhase phase,
        int turnNumber,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt = null)
    {
        Id = id;
        OperatorId = operatorId;
        Combat = combat;
        Ai = ai;
        OperatorManager = operatorManager;
        PetState = petState;
        EnemyLevel = enemyLevel;
        Seed = seed;
        Phase = phase;
        TurnNumber = turnNumber;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
    }

    public static CombatSession CreateDefault(string? playerName = null, int? seed = null, float? startingDistance = null, string? enemyName = null, Guid? id = null)
    {
        var resolvedSeed = seed ?? Random.Shared.Next();
        var name = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();
        var foeName = string.IsNullOrWhiteSpace(enemyName) ? "Enemy" : enemyName.Trim();

        var player = new Operator(name)
        {
            EquippedWeapon = WeaponFactory.CreateSokol545(),
            DistanceToOpponent = startingDistance ?? 15f
        };
        player.CurrentAmmo = player.EquippedWeapon!.MagazineSize;

        var enemy = new Operator(foeName)
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            DistanceToOpponent = startingDistance ?? 15f
        };
        enemy.CurrentAmmo = enemy.EquippedWeapon!.MagazineSize;

        var debugOptions = new CombatDebugOptions { VerboseShotLogs = false };
        var combat = new CombatSystemV2(player, enemy, seed: resolvedSeed, debugOptions: debugOptions);
        var ai = new SimpleAIV2(seed: resolvedSeed);
        var operatorManager = new OperatorManager();

        var petState = new PetState(player.Id, 100f, 0f, 0f, 0f, 100f, 0f, 100f, DateTimeOffset.UtcNow);
        var enemyLevel = Math.Max(0, new Random(resolvedSeed).Next(-2, 3));
        var operatorId = OperatorId.FromGuid(player.Id);

        return new CombatSession(
            id ?? Guid.NewGuid(),
            operatorId,
            combat,
            ai,
            operatorManager,
            petState,
            enemyLevel,
            resolvedSeed,
            SessionPhase.Planning,
            1,
            DateTimeOffset.UtcNow);
    }

    public void TransitionTo(SessionPhase nextPhase)
    {
        if (!IsValidTransition(Phase, nextPhase))
        {
            throw new InvalidOperationException($"Invalid session phase transition: {Phase} -> {nextPhase}");
        }

        Phase = nextPhase;
        
        // Record completion time when transitioning to Completed phase
        if (nextPhase == SessionPhase.Completed && CompletedAt == null)
        {
            CompletedAt = DateTimeOffset.UtcNow;
        }
    }

    public void AdvanceTurnCounter()
    {
        TurnNumber++;
    }

    /// <summary>
    /// Gets the combat outcome for this session.
    /// Can only be called after the session has completed.
    /// </summary>
    /// <returns>The combat outcome.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the session is not yet completed.</exception>
    public CombatOutcome GetOutcome()
    {
        if (Phase != SessionPhase.Completed)
        {
            throw new InvalidOperationException(
                $"Cannot produce outcome: session is not completed (current phase: {Phase})");
        }

        var player = Player;
        var enemy = Enemy;

        var operatorDied = !player.IsAlive;
        var damageTaken = player.MaxHealth - player.Health;

        // Calculate XP based on outcome
        var isVictory = player.IsAlive && !enemy.IsAlive;

        int xpGained;
        if (isVictory)
        {
            xpGained = 100; // Base XP for victory
        }
        else if (!operatorDied)
        {
            xpGained = 50; // Partial XP for surviving
        }
        else
        {
            xpGained = 0; // No XP for death
        }

        var operatorId = OperatorId.FromGuid(player.Id);

        // For now, no gear is lost (will be expanded later with actual gear system)
        var gearLost = Array.Empty<GearId>();

        return new CombatOutcome(
            sessionId: Id,
            operatorId: operatorId,
            operatorDied: operatorDied,
            xpGained: xpGained,
            gearLost: gearLost,
            isVictory: isVictory,
            turnsSurvived: TurnNumber,
            damageTaken: damageTaken,
            completedAt: CompletedAt ?? DateTimeOffset.UtcNow);
    }

    private static bool IsValidTransition(SessionPhase current, SessionPhase next)
    {
        return (current, next) switch
        {
            (SessionPhase.Created, SessionPhase.Planning) => true,
            (SessionPhase.Planning, SessionPhase.Resolving) => true,
            (SessionPhase.Resolving, SessionPhase.Planning) => true,
            (SessionPhase.Planning, SessionPhase.Completed) => true,
            (SessionPhase.Resolving, SessionPhase.Completed) => true,
            _ => current == next
        };
    }
}
