using GUNRPG.Core.Intents;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Deterministic game engine for the distributed lockstep authority.
/// Maintains a lightweight state model that tracks operator actions for hash verification.
/// Used by both <see cref="LocalGameAuthority"/> and <see cref="DistributedAuthority"/>
/// to ensure all nodes agree on the action sequence.
/// <para>
/// This engine mirrors the intent-level state changes from the existing combat system
/// (<see cref="Sessions.CombatSessionService"/>). The actual combat resolution (damage, AI,
/// phases) is handled by <see cref="Sessions.CombatSessionService"/> and
/// <see cref="Combat.DeterministicCombatEngine"/>; this engine provides the deterministic
/// action ledger used for P2P state consistency verification.
/// </para>
/// </summary>
public sealed class DefaultGameEngine : IDeterministicGameEngine
{
    public GameStateDto Step(GameStateDto state, PlayerActionDto action)
    {
        var operators = state.Operators
            .Select(op => op.OperatorId == action.OperatorId ? ApplyActionToSnapshot(op, action) : CloneSnapshot(op))
            .ToList();

        // If the operator doesn't exist yet, create and apply
        if (!operators.Any(op => op.OperatorId == action.OperatorId))
        {
            var newOp = new GameStateDto.OperatorSnapshot
            {
                OperatorId = action.OperatorId,
                Name = $"Operator-{action.OperatorId.ToString()[..8]}",
                CurrentHealth = 100f,
                MaxHealth = 100f,
                EquippedWeaponName = "Default",
                UnlockedPerks = new List<string>()
            };
            operators.Add(ApplyActionToSnapshot(newOp, action));
        }

        return new GameStateDto
        {
            ActionCount = state.ActionCount + 1,
            Operators = operators.OrderBy(op => op.OperatorId).ToList()
        };
    }

    private static GameStateDto.OperatorSnapshot ApplyActionToSnapshot(
        GameStateDto.OperatorSnapshot snapshot, PlayerActionDto action)
    {
        var health = snapshot.CurrentHealth;
        var xp = snapshot.TotalXp;

        // Mirror intent-level state changes from the combat system
        if (action.Primary == PrimaryAction.Fire)
        {
            xp += 10;
        }

        if (action.Primary == PrimaryAction.Reload)
        {
            xp += 1;
        }

        return new GameStateDto.OperatorSnapshot
        {
            OperatorId = snapshot.OperatorId,
            Name = snapshot.Name,
            TotalXp = xp,
            CurrentHealth = health,
            MaxHealth = snapshot.MaxHealth,
            EquippedWeaponName = snapshot.EquippedWeaponName,
            UnlockedPerks = snapshot.UnlockedPerks.ToList(),
            ExfilStreak = snapshot.ExfilStreak,
            IsDead = snapshot.IsDead
        };
    }

    private static GameStateDto.OperatorSnapshot CloneSnapshot(GameStateDto.OperatorSnapshot snapshot)
    {
        return new GameStateDto.OperatorSnapshot
        {
            OperatorId = snapshot.OperatorId,
            Name = snapshot.Name,
            TotalXp = snapshot.TotalXp,
            CurrentHealth = snapshot.CurrentHealth,
            MaxHealth = snapshot.MaxHealth,
            EquippedWeaponName = snapshot.EquippedWeaponName,
            UnlockedPerks = snapshot.UnlockedPerks.ToList(),
            ExfilStreak = snapshot.ExfilStreak,
            IsDead = snapshot.IsDead
        };
    }
}
