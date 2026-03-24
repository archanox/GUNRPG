using GUNRPG.Application.Combat;
using GUNRPG.Core;
using GUNRPG.Core.AI;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Equipment;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Simulation;
using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// Aggregates combat state, AI, and operator lifecycle for a single session.
/// </summary>
public sealed class CombatSession
{
    /// <summary>
    /// Schema version for FinalHash computation. Increment when the hash algorithm changes.
    /// </summary>
    public const int CurrentVersion = 1;

    private readonly List<IntentSnapshot> _replayTurns = [];

    public Guid Id { get; }
    public OperatorId OperatorId { get; }  // Reference to the operator (does not store progression)
    public CombatSystemV2 Combat { get; }
    public SimpleAIV2 Ai { get; }
    public OperatorManager OperatorManager { get; }
    public PetState PetState { get; set; }
    public int EnemyLevel { get; }
    public int PlayerLevel { get; }
    public int Seed { get; }

    /// <summary>
    /// Schema version used when computing <see cref="FinalHash"/>.
    /// </summary>
    public int Version { get; }

    public SessionPhase Phase { get; private set; }
    public int TurnNumber { get; private set; }

    /// <summary>
    /// Current simulation tick, equivalent to <see cref="TurnNumber"/>.
    /// </summary>
    public int CurrentTick => TurnNumber;

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset LastActionTimestamp { get; private set; }
    public bool PostCombatResolved { get; set; }
    public string ReplayInitialSnapshotJson { get; private set; }
    public IReadOnlyList<IntentSnapshot> ReplayTurns => _replayTurns;

    /// <summary>
    /// Deterministic hash computed from replay-critical session data when the session is
    /// finalized. Null for sessions that have not yet been completed.
    /// </summary>
    public byte[]? FinalHash { get; private set; }

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
        DateTimeOffset? completedAt = null,
        DateTimeOffset? lastActionTimestamp = null,
        string? replayInitialSnapshotJson = null,
        IEnumerable<IntentSnapshot>? replayTurns = null,
        int version = CurrentVersion,
        byte[]? finalHash = null,
        int playerLevel = 0)
    {
        Id = id;
        OperatorId = operatorId;
        Combat = combat;
        Ai = ai;
        OperatorManager = operatorManager;
        PetState = petState;
        EnemyLevel = enemyLevel;
        PlayerLevel = playerLevel;
        Seed = seed;
        Version = version > 0 ? version : CurrentVersion;
        Phase = phase;
        TurnNumber = turnNumber;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
        LastActionTimestamp = lastActionTimestamp ?? createdAt;
        ReplayInitialSnapshotJson = replayInitialSnapshotJson ?? string.Empty;
        if (replayTurns != null)
        {
            _replayTurns.AddRange(replayTurns.Select(CloneIntentSnapshot));
        }

        // Restore a stored FinalHash when reconstructing from a snapshot (defensive copy).
        FinalHash = finalHash != null ? (byte[])finalHash.Clone() : null;
    }

    public static CombatSession CreateDefault(string? playerName = null, int? seed = null, float? startingDistance = null, string? enemyName = null, Guid? id = null, Guid? operatorId = null, long? playerTotalXp = null)
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
        var enemyLevel = Math.Max(0, new SeededRandom(resolvedSeed).Next(-2, 3));
        var resolvedPlayerLevel = OpponentDifficulty.ComputeLevelFromXp(playerTotalXp ?? 0L);
        var resolvedOperatorId = operatorId.HasValue
            ? OperatorId.FromGuid(operatorId.Value)
            : OperatorId.FromGuid(player.Id);

        return new CombatSession(
            id ?? Guid.NewGuid(),
            resolvedOperatorId,
            combat,
            ai,
            operatorManager,
            petState,
            enemyLevel,
            resolvedSeed,
            SessionPhase.Planning,
            1,
            DateTimeOffset.UtcNow,
            playerLevel: resolvedPlayerLevel);
    }

    public void TransitionTo(SessionPhase nextPhase, DateTimeOffset? timestamp = null)
    {
        if (!IsValidTransition(Phase, nextPhase))
        {
            throw new InvalidOperationException($"Invalid session phase transition: {Phase} -> {nextPhase}");
        }

        var resolvedTimestamp = timestamp ?? DateTimeOffset.UtcNow;
        Phase = nextPhase;
        LastActionTimestamp = resolvedTimestamp;
        
        // Record completion time and compute FinalHash when transitioning to Completed phase.
        if (nextPhase == SessionPhase.Completed && CompletedAt == null)
        {
            CompletedAt = resolvedTimestamp;
            FinalHash = CombatSessionHasher.ComputeHash(Id, Seed, Version, TurnNumber, _replayTurns);
        }
    }

    public void AdvanceTurnCounter(DateTimeOffset? timestamp = null)
    {
        if (Phase == SessionPhase.Completed)
        {
            throw new InvalidOperationException("Cannot advance turn counter: session is already completed.");
        }

        TurnNumber++;
        LastActionTimestamp = timestamp ?? DateTimeOffset.UtcNow;
    }

    public void RecordAction(DateTimeOffset? timestamp = null)
    {
        LastActionTimestamp = timestamp ?? DateTimeOffset.UtcNow;
    }

    public void SetReplayInitialSnapshotJson(string snapshotJson)
    {
        ReplayInitialSnapshotJson = snapshotJson ?? string.Empty;
    }

    public void RecordReplayTurn(SimultaneousIntents intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        if (Phase == SessionPhase.Completed)
        {
            throw new InvalidOperationException("Cannot record replay turn: session is already completed.");
        }

        _replayTurns.Add(new IntentSnapshot
        {
            OperatorId = intents.OperatorId,
            Primary = intents.Primary,
            Movement = intents.Movement,
            Stance = intents.Stance,
            Cover = intents.Cover,
            CancelMovement = intents.CancelMovement,
            SubmittedAtMs = intents.SubmittedAtMs
        });
    }

    /// <summary>
    /// Rebuilds the session state by replaying all recorded turns from the initial snapshot.
    /// Returns the replay-derived final snapshot, or <c>null</c> if no initial snapshot JSON
    /// is recorded (e.g. legacy sessions created before the replay system was introduced).
    /// </summary>
    /// <remarks>
    /// Call <see cref="CombatSessionHasher.ComputeStateHash"/> on the returned snapshot and compare
    /// it with <see cref="FinalHash"/> to verify that the stored state is consistent with the
    /// replay log.
    /// </remarks>
    public async Task<CombatSessionSnapshot?> RebuildStateAsync()
    {
        if (string.IsNullOrEmpty(ReplayInitialSnapshotJson))
        {
            return null;
        }

        var result = await OfflineCombatReplay.ReplayAsync(ReplayInitialSnapshotJson, _replayTurns);
        return result.FinalSnapshot;
    }

    /// <summary>
    /// Computes and sets <see cref="FinalHash"/> from the authoritative replayed final state.
    /// For sessions with a recorded <see cref="ReplayInitialSnapshotJson"/>, the full replay is
    /// executed and the resulting simulation output is hashed via
    /// <see cref="CombatSessionHasher.ComputeStateHash"/>, guaranteeing that
    /// <c>FinalHash == hash(replay(ReplayTurns, Seed))</c>.
    /// Falls back to the input-based <see cref="CombatSessionHasher.ComputeHash"/> for legacy
    /// or test sessions that were created without an initial snapshot.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the session is not completed.</exception>
    public async Task FinalizeAsync()
    {
        if (Phase != SessionPhase.Completed)
        {
            throw new InvalidOperationException(
                $"Cannot finalize session: not yet completed (current phase: {Phase}).");
        }

        if (!string.IsNullOrEmpty(ReplayInitialSnapshotJson))
        {
            var result = await OfflineCombatReplay.ReplayAsync(ReplayInitialSnapshotJson, _replayTurns);
            FinalHash = CombatSessionHasher.ComputeStateHash(result.FinalSnapshot);
        }
        // else: keep the input-based hash already set by TransitionTo(SessionPhase.Completed)
        // via CombatSessionHasher.ComputeHash. This covers legacy and test sessions that were
        // created via CreateDefault() without a ReplayInitialSnapshotJson.
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

        var operatorId = OperatorId;

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
            completedAt: CompletedAt ?? (CreatedAt + ToBoundedCombatDuration(Combat.CurrentTimeMs)));
    }

    internal static TimeSpan ToBoundedCombatDuration(long currentTimeMs)
    {
        var boundedMilliseconds = Math.Clamp(currentTimeMs, 0L, (long)TimeSpan.MaxValue.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(boundedMilliseconds);
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

    private static IntentSnapshot CloneIntentSnapshot(IntentSnapshot snapshot)
    {
        return new IntentSnapshot
        {
            OperatorId = snapshot.OperatorId,
            Primary = snapshot.Primary,
            Movement = snapshot.Movement,
            Stance = snapshot.Stance,
            Cover = snapshot.Cover,
            CancelMovement = snapshot.CancelMovement,
            SubmittedAtMs = snapshot.SubmittedAtMs
        };
    }
}
