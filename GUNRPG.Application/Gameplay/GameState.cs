using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Gameplay;

public sealed class GameState
{
    public IReadOnlyList<PlayerState> Players { get; init; } = [];

    public IReadOnlyList<RunHistoryEntry> RunHistory { get; init; } = [];

    public sealed class PlayerState
    {
        public Guid PlayerId { get; init; }

        public string Name { get; init; } = string.Empty;

        public long TotalXp { get; init; }

        public float CurrentHealth { get; init; }

        public float MaxHealth { get; init; }

        public string EquippedWeaponName { get; init; } = string.Empty;

        public IReadOnlyList<string> Inventory { get; init; } = [];

        public IReadOnlyList<string> UnlockedPerks { get; init; } = [];

        public int ExfilStreak { get; init; }

        public bool IsDead { get; init; }

        public string CurrentMode { get; init; } = string.Empty;

        public Guid? InfilSessionId { get; init; }

        public Guid? ActiveCombatSessionId { get; init; }

        public string LockedLoadout { get; init; } = string.Empty;

        public PetState? PetState { get; init; }
    }

    public sealed class RunHistoryEntry
    {
        public Guid RunId { get; init; }

        public Guid PlayerId { get; init; }

        public DateTimeOffset Timestamp { get; init; }

        public string Outcome { get; init; } = string.Empty;

        public IReadOnlyList<string> Events { get; init; } = [];
    }
}
