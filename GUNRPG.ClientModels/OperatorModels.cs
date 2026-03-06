namespace GUNRPG.ClientModels;

/// <summary>
/// Operator summary returned from GET /operators.
/// </summary>
public sealed class OperatorSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CurrentMode { get; init; } = "Base";
    public bool IsDead { get; init; }
    public long TotalXp { get; init; }
    public float CurrentHealth { get; init; }
    public float MaxHealth { get; init; }
}

/// <summary>
/// Full operator state returned from GET /operators/{id}.
/// </summary>
public sealed class OperatorState
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public long TotalXp { get; init; }
    public float CurrentHealth { get; init; }
    public float MaxHealth { get; init; }
    public string EquippedWeaponName { get; init; } = string.Empty;
    public List<string> UnlockedPerks { get; init; } = new();
    public int ExfilStreak { get; init; }
    public bool IsDead { get; init; }
    public string CurrentMode { get; init; } = "Base";
    public DateTimeOffset? InfilStartTime { get; init; }
    public Guid? InfilSessionId { get; init; }
    public Guid? ActiveCombatSessionId { get; init; }
    public CombatSession? ActiveCombatSession { get; init; }
    public string LockedLoadout { get; init; } = string.Empty;
    public PetState? Pet { get; init; }

    /// <summary>Approximate level derived from total XP (100 XP per level).</summary>
    public int Level => TotalXp > 0 ? (int)(TotalXp / 100) + 1 : 1;

    /// <summary>True when the operator is deployed in the field (Infil mode).</summary>
    public bool IsOnMission => CurrentMode == "Infil";
}

/// <summary>
/// Response returned from POST /operators/{id}/infil/start.
/// </summary>
public sealed class StartInfilResponse
{
    public Guid SessionId { get; init; }
    public OperatorState Operator { get; init; } = null!;
}
