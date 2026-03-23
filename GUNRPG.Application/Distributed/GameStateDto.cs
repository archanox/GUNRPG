using GUNRPG.Application.Combat;
using GUNRPG.Application.Sessions;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Represents the full game state for hashing and deterministic verification.
/// Contains all state that must be identical across distributed nodes.
/// </summary>
public sealed class GameStateDto
{
    public long ActionCount { get; init; }
    public List<OperatorSnapshot> Operators { get; init; } = new();
    public List<CombatSessionState> Sessions { get; init; } = new();

    public GameStateDto Clone()
    {
        return new GameStateDto
        {
            ActionCount = ActionCount,
            Operators = Operators.Select(op => op.Clone()).ToList(),
            Sessions = Sessions.Select(session => session.Clone()).ToList()
        };
    }

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

        public OperatorSnapshot Clone()
        {
            return new OperatorSnapshot
            {
                OperatorId = OperatorId,
                Name = Name,
                TotalXp = TotalXp,
                CurrentHealth = CurrentHealth,
                MaxHealth = MaxHealth,
                EquippedWeaponName = EquippedWeaponName,
                UnlockedPerks = UnlockedPerks.ToList(),
                ExfilStreak = ExfilStreak,
                IsDead = IsDead
            };
        }
    }

    /// <summary>
    /// Replay-backed combat session state used for authority hashing.
    /// </summary>
    public sealed class CombatSessionState
    {
        public Guid SessionId { get; init; }
        public Guid OperatorId { get; init; }
        public CombatSessionSnapshot Snapshot { get; init; } = default!;
        public string SnapshotHash { get; init; } = string.Empty;
        public CombatOutcome? Outcome { get; init; }

        public CombatSessionState Clone()
        {
            return new CombatSessionState
            {
                SessionId = SessionId,
                OperatorId = OperatorId,
                Snapshot = CopySnapshot(Snapshot),
                SnapshotHash = SnapshotHash,
                Outcome = Outcome
            };
        }
    }

    private static CombatSessionSnapshot CopySnapshot(CombatSessionSnapshot snapshot)
    {
        return new CombatSessionSnapshot
        {
            Id = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Phase = snapshot.Phase,
            TurnNumber = snapshot.TurnNumber,
            Combat = snapshot.Combat,
            Player = snapshot.Player,
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
            EnemyLevel = snapshot.EnemyLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = snapshot.CreatedAt,
            CompletedAt = snapshot.CompletedAt,
            LastActionTimestamp = snapshot.LastActionTimestamp,
            ReplayInitialSnapshotJson = snapshot.ReplayInitialSnapshotJson,
            ReplayTurns = snapshot.ReplayTurns.ToList(),
            Version = snapshot.Version,
            FinalHash = snapshot.FinalHash != null ? (byte[])snapshot.FinalHash.Clone() : null
        };
    }
}
