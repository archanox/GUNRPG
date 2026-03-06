namespace GUNRPG.WebClient.Models;

public sealed class CombatSession
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public long CurrentTimeMs { get; set; }
    public PlayerState Player { get; set; } = new();
    public PlayerState Enemy { get; set; } = new();
    public int EnemyLevel { get; set; }
    public int TurnNumber { get; set; }
    public List<BattleLogEntry> BattleLog { get; set; } = new();

    public bool IsAwaitingIntents => Phase == "AwaitingPlayerIntents";
    public bool IsConcluded => Phase is "Victory" or "Defeat";
    public bool IsVictory => Phase == "Victory";
}

public sealed class PlayerState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public float Stamina { get; set; }
    public float Fatigue { get; set; }
    public float SuppressionLevel { get; set; }
    public bool IsSuppressed { get; set; }
    public float DistanceToOpponent { get; set; }
    public int CurrentAmmo { get; set; }
    public int? MagazineSize { get; set; }
    public string AimState { get; set; } = string.Empty;
    public string MovementState { get; set; } = string.Empty;
    public string CurrentMovement { get; set; } = string.Empty;
    public string CurrentDirection { get; set; } = string.Empty;
    public string CurrentCover { get; set; } = string.Empty;
    public bool IsMoving { get; set; }
    public bool IsAlive { get; set; }
}

public sealed class BattleLogEntry
{
    public string EventType { get; set; } = string.Empty;
    public long TimeMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ActorName { get; set; }
}

public sealed class IntentRequest
{
    public IntentDto Intents { get; set; } = new();
    public Guid? OperatorId { get; set; }
}

public sealed class IntentDto
{
    public string? Primary { get; set; }
    public string? Movement { get; set; }
    public string? Stance { get; set; }
    public string? Cover { get; set; }
    public bool CancelMovement { get; set; }
}
