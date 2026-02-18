namespace GUNRPG.Api.Dtos;

/// <summary>
/// API-specific operator aggregate state DTO.
/// </summary>
public sealed class ApiOperatorStateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public long TotalXp { get; init; }
    public float CurrentHealth { get; init; }
    public float MaxHealth { get; init; }
    public string EquippedWeaponName { get; init; } = "";
    public List<string> UnlockedPerks { get; init; } = new();
    public int ExfilStreak { get; init; }
    public bool IsDead { get; init; }
    public string CurrentMode { get; init; } = "";
    public DateTimeOffset? InfilStartTime { get; init; }
    public Guid? InfilSessionId { get; init; }
    public Guid? ActiveCombatSessionId { get; init; }
    public ApiCombatSessionDto? ActiveCombatSession { get; init; }
    public string LockedLoadout { get; init; } = "";
    public ApiPetStateDto? Pet { get; init; }
}
