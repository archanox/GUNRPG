using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Dtos;

/// <summary>
/// DTO representing operator aggregate state (for UI display).
/// This differs from player state in combat sessions.
/// </summary>
public sealed class OperatorStateDto
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
    public OperatorMode CurrentMode { get; init; }
    public DateTimeOffset? InfilStartTime { get; init; }
    public Guid? ActiveSessionId { get; init; }
    public string LockedLoadout { get; init; } = string.Empty;
}
