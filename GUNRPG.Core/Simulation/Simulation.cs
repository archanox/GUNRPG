using GUNRPG.Core.Time;

namespace GUNRPG.Core.Simulation;

public static class Simulation
{
    private const int PlayerHitChancePct = 60;
    private const int EnemyHitChancePct = 50;
    private const int PlayerMinDamage = 10;
    private const int PlayerMaxDamage = 30;
    private const int EnemyMinDamage = 5;
    private const int EnemyMaxDamage = 20;
    private const int ItemHealAmount = 20;

    public static SimulationState Step(SimulationState state, PlayerAction input, long tick)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);

        if (state.Time.CurrentTimeMs != tick)
        {
            throw new InvalidOperationException(
                $"Simulation tick mismatch. Expected tick {state.Time.CurrentTimeMs}, received {tick}.");
        }

        var random = new SeededRandom(state.Random.Seed, state.Random.CallCount);
        var player = state.Player;
        var enemies = state.Enemies.OrderBy(enemy => enemy.Id).ToList();
        var emittedEvents = new List<SimulationEvent>();
        var pendingEffects = new EventQueue<PendingEffect>();
        var nextSequence = 0;

        switch (input)
        {
            case MoveAction move:
                emittedEvents.Add(new InfilStateChangedSimulationEvent("Moving", move.Direction.ToString()));
                break;

            case AttackAction attack:
                if (random.Next(0, 100) < PlayerHitChancePct)
                {
                    var damage = PlayerMinDamage + random.Next(0, PlayerMaxDamage - PlayerMinDamage + 1);
                    pendingEffects.Schedule(tick, nextSequence++, new PendingEffect(
                        EffectKind.DamageEnemy,
                        damage,
                        targetEnemyId: 1,
                        reason: attack.TargetId.ToString("N")));
                }
                break;

            case UseItemAction useItem:
                emittedEvents.Add(new ItemAcquiredSimulationEvent(useItem.ItemId.ToString("N")));
                pendingEffects.Schedule(tick, nextSequence++, new PendingEffect(
                    EffectKind.HealPlayer,
                    ItemHealAmount,
                    reason: useItem.ItemId.ToString("N")));
                break;

            case ExfilAction:
                emittedEvents.Add(new RunCompletedSimulationEvent(true, "Exfil"));
                break;
        }

        while (pendingEffects.Count > 0)
        {
            var scheduledEffect = pendingEffects.DequeueNext()!;
            var effect = scheduledEffect.Value;

            switch (effect.Kind)
            {
                case EffectKind.DamagePlayer:
                    player = player.WithDamage(effect.Amount);
                    break;
                case EffectKind.HealPlayer:
                    player = player.WithHealing(effect.Amount);
                    emittedEvents.Add(new PlayerHealedSimulationEvent(effect.Amount, effect.Reason));
                    break;
                case EffectKind.DamageEnemy:
                    var idx = enemies.FindIndex(enemy => enemy.Id == effect.TargetEnemyId && enemy.IsAlive);
                    if (idx < 0)
                    {
                        idx = enemies.FindIndex(enemy => enemy.IsAlive);
                    }

                    if (idx >= 0)
                    {
                        enemies[idx] = enemies[idx].WithDamage(effect.Amount);
                        emittedEvents.Add(new EnemyDamagedSimulationEvent(effect.Amount, effect.Reason));
                    }
                    break;
            }
        }

        if (player.IsAlive)
        {
            foreach (var enemy in enemies)
            {
                if (!enemy.IsAlive || !player.IsAlive)
                {
                    continue;
                }

                if (random.Next(0, 100) < EnemyHitChancePct)
                {
                    var damage = EnemyMinDamage + random.Next(0, EnemyMaxDamage - EnemyMinDamage + 1);
                    player = player.WithDamage(damage);
                    emittedEvents.Add(new PlayerDamagedSimulationEvent(damage, "EnemyAttack"));
                }
            }
        }

        enemies = enemies
            .Where(enemy => enemy.IsAlive)
            .OrderBy(enemy => enemy.Id)
            .ToList();

        var updatedTime = new SimulationTime(state.Time.CurrentTimeMs + 1);

        var allEvents = state.Events.Concat(emittedEvents).ToArray();
        return new SimulationState(
            updatedTime,
            new RngState(state.Random.Seed, random.CallCount),
            player,
            enemies,
            allEvents,
            emittedEvents);
    }
}
