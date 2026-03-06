namespace GUNRPG.WebClient.Models;

public sealed class OperatorSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrentMode { get; set; } = string.Empty;
    public bool IsDead { get; set; }
    public long TotalXp { get; set; }
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
}

public sealed class OperatorState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long TotalXp { get; set; }
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public string EquippedWeaponName { get; set; } = string.Empty;
    public List<string> UnlockedPerks { get; set; } = new();
    public int ExfilStreak { get; set; }
    public bool IsDead { get; set; }
    public string CurrentMode { get; set; } = string.Empty;
    public DateTimeOffset? InfilStartTime { get; set; }
    public Guid? InfilSessionId { get; set; }
    public Guid? ActiveCombatSessionId { get; set; }
    public CombatSession? ActiveCombatSession { get; set; }

    public int Level => TotalXp > 0 ? (int)(TotalXp / 100) + 1 : 1;
    public bool IsOnMission => CurrentMode is "Infil" or "InCombat";
}

public sealed class OperatorCreateRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class StartInfilResponse
{
    public Guid SessionId { get; set; }
    public OperatorState Operator { get; set; } = null!;
}
