using GUNRPG.Application.Backend;
using GUNRPG.Application.Dtos;

namespace GUNRPG.Application.Combat;

/// <summary>
/// Pure deterministic combat simulation.
/// All randomness derives from the provided seed via <c>new Random(seed)</c>.
/// Uses no external state, no <c>DateTime.Now</c>, and no static <c>Random</c>.
/// Offline and server both reference this same engine to ensure replay parity.
/// </summary>
public sealed class DeterministicCombatEngine : IDeterministicCombatEngine
{
    private const int VictoryXpReward = 100;
    private const int SurvivalXpReward = 50;
    private const int DeathXpReward = 0;
    private const int MaxRounds = 20;

    /// <inheritdoc />
    public CombatResult Execute(OperatorDto snapshot, int seed)
    {
        var rng = new Random(seed);
        var log = new List<BattleLogEntryDto>();
        long timeMs = 0;

        // Enemy stats derived deterministically from seed
        var enemyHealth = 80f + rng.Next(0, 60);
        var playerHealth = snapshot.CurrentHealth;

        for (int round = 1; round <= MaxRounds && playerHealth > 0 && enemyHealth > 0; round++)
        {
            timeMs += 1000;

            // Player attacks
            if (rng.NextDouble() < 0.65)
            {
                var dmg = 8f + rng.Next(0, 17);
                enemyHealth -= dmg;
                log.Add(new BattleLogEntryDto
                {
                    EventType = "Damage",
                    TimeMs = timeMs,
                    Message = $"Enemy took {dmg:F1} damage",
                    ActorName = snapshot.Name
                });
            }
            else
            {
                log.Add(new BattleLogEntryDto
                {
                    EventType = "Miss",
                    TimeMs = timeMs,
                    Message = $"{snapshot.Name} missed",
                    ActorName = snapshot.Name
                });
            }

            if (enemyHealth <= 0)
                break;

            // Enemy attacks
            if (rng.NextDouble() < 0.55)
            {
                var dmg = 5f + rng.Next(0, 15);
                playerHealth -= dmg;
                log.Add(new BattleLogEntryDto
                {
                    EventType = "Damage",
                    TimeMs = timeMs,
                    Message = $"{snapshot.Name} took {dmg:F1} damage",
                    ActorName = "Enemy"
                });
            }
            else
            {
                log.Add(new BattleLogEntryDto
                {
                    EventType = "Miss",
                    TimeMs = timeMs,
                    Message = "Enemy missed",
                    ActorName = "Enemy"
                });
            }
        }

        var operatorDied = playerHealth <= 0;
        var isVictory = !operatorDied && enemyHealth <= 0;
        var xpGained = isVictory ? VictoryXpReward : operatorDied ? DeathXpReward : SurvivalXpReward;

        var resultOperator = new OperatorDto
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            TotalXp = snapshot.TotalXp + xpGained,
            CurrentHealth = operatorDied ? snapshot.MaxHealth : Math.Max(1f, playerHealth),
            MaxHealth = snapshot.MaxHealth,
            EquippedWeaponName = snapshot.EquippedWeaponName,
            UnlockedPerks = snapshot.UnlockedPerks,
            ExfilStreak = snapshot.ExfilStreak,
            IsDead = false,
            CurrentMode = operatorDied ? "Base" : "Infil",
            ActiveCombatSessionId = null,
            InfilSessionId = operatorDied ? null : snapshot.InfilSessionId,
            InfilStartTime = operatorDied ? null : snapshot.InfilStartTime,
            LockedLoadout = snapshot.LockedLoadout,
            Pet = snapshot.Pet
        };

        return new CombatResult
        {
            ResultOperator = resultOperator,
            IsVictory = isVictory,
            OperatorDied = operatorDied,
            BattleLog = log
        };
    }
}
