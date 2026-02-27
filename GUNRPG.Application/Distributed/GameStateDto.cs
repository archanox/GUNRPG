namespace GUNRPG.Application.Distributed;

/// <summary>
/// Represents the full game state for hashing and deterministic verification.
/// Contains all state that must be identical across distributed nodes.
/// </summary>
public sealed class GameStateDto
{
    public long ActionCount { get; init; }
    public List<OperatorSnapshot> Operators { get; init; } = new();

    /// <summary>
    /// A snapshot of a single operator's state within the distributed game.
    /// </summary>
    public sealed class OperatorSnapshot
    {
        public Guid OperatorId { get; init; }
        public string Name { get; init; } = string.Empty;
        public long TotalXp { get; init; }
        public float CurrentHealth { get; init; }
        public float MaxHealth { get; init; }
        public string EquippedWeaponName { get; init; } = string.Empty;
        public List<string> UnlockedPerks { get; init; } = new();
        public int ExfilStreak { get; init; }
        public bool IsDead { get; init; }
    }
}
