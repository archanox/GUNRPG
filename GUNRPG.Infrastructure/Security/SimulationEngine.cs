using GUNRPG.Application.Gameplay;

namespace GUNRPG.Security;

/// <summary>
/// Deterministic tick-based combat simulation engine.
/// <para>
/// All randomness is derived from a single <see cref="Random"/> instance seeded by
/// <see cref="RunInput.Seed"/>. No <c>DateTime</c>, <c>Guid.NewGuid()</c>, parallel
/// execution, or floating-point arithmetic is used — the same <see cref="RunInput"/>
/// always produces byte-identical <see cref="GameplayLedgerEvent"/> sequences on every
/// machine and across every replay.
/// </para>
/// <para>
/// Loop per action:
/// <list type="number">
///   <item>ProcessAction — translate player intent into queued effects and immediate events.</item>
///   <item>AdvanceSimulation — ResolveCombat → ApplyEffects → RunAI → Cleanup → Tick++.</item>
/// </list>
/// </para>
/// </summary>
internal static class SimulationEngine
{
    // ── Combat constants (integers only — no floating-point arithmetic) ──────────

    private const int DefaultPlayerHealth = 100;
    private const int DefaultPlayerMaxHealth = 100;

    private const int DefaultEnemyHealth = 100;
    private const int DefaultEnemyMaxHealth = 100;

    /// <summary>Player hit-chance expressed as an integer percentage (0–100).</summary>
    private const int PlayerHitChancePct = 60;

    /// <summary>Enemy AI hit-chance expressed as an integer percentage (0–100).</summary>
    private const int EnemyHitChancePct = 50;

    private const int PlayerMinDamage = 10;
    private const int PlayerMaxDamage = 30;
    private const int PlayerDamageRange = PlayerMaxDamage - PlayerMinDamage + 1;

    private const int EnemyMinDamage = 5;
    private const int EnemyMaxDamage = 20;
    private const int EnemyDamageRange = EnemyMaxDamage - EnemyMinDamage + 1;

    private const int ItemHealAmount = 20;

    // ── Public entry point ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full deterministic simulation for the supplied player actions and returns
    /// all emitted <see cref="GameplayLedgerEvent"/>s in the order they were produced.
    /// </summary>
    /// <param name="actions">Ordered list of player actions (no nulls).</param>
    /// <param name="rng">
    ///   Seeded <see cref="Random"/> instance — must be the only source of randomness
    ///   and must not be shared with any other code path.
    /// </param>
    public static IReadOnlyList<GameplayLedgerEvent> Run(
        IReadOnlyList<PlayerAction> actions,
        Random rng)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(rng);

        var player = new SimulationPlayerState(DefaultPlayerHealth, DefaultPlayerMaxHealth);
        var enemies = new List<SimulationEnemyState>
        {
            new(1, DefaultEnemyHealth, DefaultEnemyMaxHealth)
        };

        var state = new SimulationState(rng, player, enemies);

        foreach (var action in actions)
        {
            ProcessAction(state, action);
            AdvanceSimulation(state);
        }

        return state.Events;
    }

    // ── Per-tick pipeline ────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 0: Translate the player's action into queued pending effects and immediate
    /// (non-combat) events.  No state mutation occurs here.
    /// </summary>
    private static void ProcessAction(SimulationState state, PlayerAction action)
    {
        switch (action)
        {
            case MoveAction move:
                state.EmitEvent(new InfilStateChangedLedgerEvent("Moving", move.Direction.ToString()));
                break;

            case AttackAction attack:
                // Determine hit/miss using integer RNG — no floating-point arithmetic.
                var hitRoll = state.Rng.Next(0, 100);
                if (hitRoll < PlayerHitChancePct)
                {
                    var damage = PlayerMinDamage + state.Rng.Next(0, PlayerDamageRange);
                    state.AddPendingEffect(
                        new PendingEffect(PendingEffect.DamageEnemy, damage, targetEnemyId: 1,
                            reason: attack.TargetId.ToString("N")));
                }
                break;

            case UseItemAction useItem:
                state.EmitEvent(new ItemAcquiredLedgerEvent(useItem.ItemId.ToString("N")));
                state.AddPendingEffect(
                    new PendingEffect(PendingEffect.HealPlayer, ItemHealAmount,
                        reason: useItem.ItemId.ToString("N")));
                break;

            case ExfilAction:
                state.EmitEvent(new RunCompletedLedgerEvent(true, "Exfil"));
                break;
        }
    }

    /// <summary>
    /// Advances the simulation by one tick:
    /// ResolveCombat → ApplyEffects → RunAI → Cleanup → Tick++.
    /// </summary>
    private static void AdvanceSimulation(SimulationState state)
    {
        ResolveCombat(state);
        ApplyEffects(state);
        RunAI(state);
        Cleanup(state);
        state.IncrementTick();
    }

    /// <summary>
    /// Phase 1: Emit gameplay events for pending player-attack effects.
    /// State is not mutated here; that happens in <see cref="ApplyEffects"/>.
    /// </summary>
    private static void ResolveCombat(SimulationState state)
    {
        foreach (var effect in state.PendingEffects)
        {
            if (effect.EffectType != PendingEffect.DamageEnemy)
                continue;

            // Emit a typed ledger event for every player attack that will land.
            state.EmitEvent(new EnemyDamagedLedgerEvent(effect.Amount, effect.Reason));
        }
    }

    /// <summary>
    /// Phase 2: Apply all queued pending effects to the simulation state, then clear
    /// the pending-effects list.  Effects are applied in stable insertion order.
    /// </summary>
    private static void ApplyEffects(SimulationState state)
    {
        var updatedEnemies = state.Enemies.ToList();

        foreach (var effect in state.PendingEffects)
        {
            switch (effect.EffectType)
            {
                case PendingEffect.DamagePlayer:
                    state.UpdatePlayer(state.Player.WithDamage(effect.Amount));
                    break;

                case PendingEffect.HealPlayer:
                    state.UpdatePlayer(state.Player.WithHealing(effect.Amount));
                    state.EmitEvent(new PlayerHealedLedgerEvent(effect.Amount, effect.Reason));
                    break;

                case PendingEffect.DamageEnemy:
                    var idx = updatedEnemies.FindIndex(e => e.Id == effect.TargetEnemyId && e.IsAlive);
                    if (idx < 0)
                    {
                        // Fall back to first alive enemy if the specific target is already dead.
                        idx = updatedEnemies.FindIndex(e => e.IsAlive);
                    }
                    if (idx >= 0)
                    {
                        updatedEnemies[idx] = updatedEnemies[idx].WithDamage(effect.Amount);
                    }
                    break;
            }
        }

        state.UpdateEnemies(updatedEnemies);
        state.ClearPendingEffects();
    }

    /// <summary>
    /// Phase 3: Each alive enemy makes a decision based solely on the current simulation
    /// state and the seeded RNG.  No external randomness is used.
    /// Enemies are processed in deterministic ascending ID order.
    /// </summary>
    private static void RunAI(SimulationState state)
    {
        if (!state.Player.IsAlive)
            return;

        // Deterministic ordering: ascending by Id (stable, integer-keyed).
        foreach (var enemy in state.Enemies.OrderBy(e => e.Id))
        {
            if (!enemy.IsAlive)
                continue;
            if (!state.Player.IsAlive)
                break;

            var hitRoll = state.Rng.Next(0, 100);
            if (hitRoll < EnemyHitChancePct)
            {
                var damage = EnemyMinDamage + state.Rng.Next(0, EnemyDamageRange);
                state.UpdatePlayer(state.Player.WithDamage(damage));
                state.EmitEvent(new PlayerDamagedLedgerEvent(damage, "EnemyAttack"));
            }
        }
    }

    /// <summary>
    /// Phase 4: Remove dead enemies from the simulation.
    /// Dead enemies are never resurrected, so removal is permanent.
    /// </summary>
    private static void Cleanup(SimulationState state)
    {
        var alive = state.Enemies.Where(e => e.IsAlive).ToList();
        state.UpdateEnemies(alive);
    }
}
