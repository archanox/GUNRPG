namespace GUNRPG.Application.Backend;

/// <summary>
/// Backend-level operator representation used by IGameBackend.
/// Contains the operator snapshot data for both online and offline modes.
/// </summary>
public sealed class OperatorDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long TotalXp { get; set; }
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public string EquippedWeaponName { get; set; } = string.Empty;
    public List<string> UnlockedPerks { get; set; } = new();
    public int ExfilStreak { get; set; }
    public bool IsDead { get; set; }
    public string CurrentMode { get; set; } = string.Empty;
    public Guid? ActiveCombatSessionId { get; set; }
    public Guid? InfilSessionId { get; set; }
}
