namespace GUNRPG.ClientModels;

/// <summary>
/// Combat session state returned from GET /sessions/{id}/state.
/// Phase comes from SessionPhase.ToString(): "Created", "Planning", "Resolving", "Completed".
/// </summary>
public sealed class CombatSession
{
    public Guid Id { get; init; }
    public Guid OperatorId { get; init; }
    public string Phase { get; init; } = string.Empty;
    public long CurrentTimeMs { get; init; }
    public PlayerState Player { get; init; } = new();
    public PlayerState Enemy { get; init; } = new();
    public PetState Pet { get; init; } = new();
    public int EnemyLevel { get; init; }
    public int TurnNumber { get; init; }
    public List<BattleLogEntry> BattleLog { get; init; } = new();

    /// <summary>True when the server is waiting for player intents (SessionPhase.Planning).</summary>
    public bool IsAwaitingIntents => Phase == "Planning";

    /// <summary>True when the combat session has fully resolved (SessionPhase.Completed).</summary>
    public bool IsConcluded => Phase == "Completed";

    /// <summary>Victory is determined from state once the session has concluded.</summary>
    public bool IsVictory => IsConcluded && Player.IsAlive && !Enemy.IsAlive;
}

/// <summary>
/// Player or enemy state within a combat session.
/// </summary>
public sealed class PlayerState
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public float Health { get; init; }
    public float MaxHealth { get; init; }
    public float Stamina { get; init; }
    public float Fatigue { get; init; }
    public float SuppressionLevel { get; init; }
    public bool IsSuppressed { get; init; }
    public float DistanceToOpponent { get; init; }
    public int CurrentAmmo { get; init; }
    public int? MagazineSize { get; init; }
    public string AimState { get; init; } = string.Empty;
    public string MovementState { get; init; } = string.Empty;
    public string CurrentMovement { get; init; } = string.Empty;
    public string CurrentDirection { get; init; } = string.Empty;
    public string CurrentCover { get; init; } = string.Empty;
    public bool IsMoving { get; init; }
    public bool IsAlive { get; init; }
}

/// <summary>
/// Pet state, used both within combat sessions and operator state.
/// </summary>
public sealed class PetState
{
    public float Health { get; init; }
    public float Fatigue { get; init; }
    public float Injury { get; init; }
    public float Stress { get; init; }
    public float Morale { get; init; }
    public float Hunger { get; init; }
    public float Hydration { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}

/// <summary>
/// A single battle log entry within a combat session.
/// </summary>
public sealed class BattleLogEntry
{
    public string EventType { get; init; } = string.Empty;
    public long TimeMs { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ActorName { get; init; }
}
