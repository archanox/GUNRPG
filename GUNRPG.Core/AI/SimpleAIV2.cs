using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;

namespace GUNRPG.Core.AI;

/// <summary>
/// AI for enemy decision making with simultaneous intent support.
/// Makes tactical decisions based on operator state and opponent state.
/// </summary>
public class SimpleAIV2
{
    private readonly Random _random;

    // AI decision constants
    private const int LOW_AMMO_THRESHOLD = 5;
    private const int LOW_HEALTH_THRESHOLD = 30;
    private const int REGEN_WAIT_HEALTH_THRESHOLD = 40;

    public SimpleAIV2(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Decides the next simultaneous intents for the AI operator.
    /// </summary>
    public SimultaneousIntents DecideIntents(Operator self, Operator opponent, CombatSystemV2 combat)
    {
        var intents = new SimultaneousIntents(self.Id);

        // Decide primary action
        intents.Primary = DecidePrimaryAction(self, opponent, combat);

        // Decide movement
        intents.Movement = DecideMovement(self, opponent, combat);

        // Decide tactical posture
        intents.Posture = DecidePosture(self, opponent, combat);

        // Decide stance
        intents.Stance = DecideStance(self, opponent, combat);

        return intents;
    }

    private PrimaryAction DecidePrimaryAction(Operator self, Operator opponent, CombatSystemV2 combat)
    {
        // Priority 1: Reload if out of ammo
        if (self.CurrentAmmo == 0 && self.WeaponState == WeaponState.Ready)
        {
            return PrimaryAction.Reload;
        }

        // Priority 2: Reload if low on ammo and opponent is far or reloading
        if (self.CurrentAmmo < LOW_AMMO_THRESHOLD &&
            self.WeaponState == WeaponState.Ready &&
            (self.DistanceToOpponent > 15 || opponent.WeaponState == WeaponState.Reloading))
        {
            return PrimaryAction.Reload;
        }

        // Priority 3: Fire if in range and have ammo
        if (self.CurrentAmmo > 0 &&
            self.WeaponState == WeaponState.Ready &&
            self.DistanceToOpponent < 30)
        {
            // Fire if opponent is visible and in reasonable range
            return PrimaryAction.Fire;
        }

        return PrimaryAction.None;
    }

    private MovementAction DecideMovement(Operator self, Operator opponent, CombatSystemV2 combat)
    {
        // Priority 1: Survive - if low health and not regenerating, create distance
        if (self.Health < LOW_HEALTH_THRESHOLD && !self.CanRegenerateHealth(combat.CurrentTimeMs))
        {
            return self.Stamina > 50 ? MovementAction.Sprint : MovementAction.Walk;
        }

        // Priority 2: If health is low and can regenerate, stop and wait
        if (self.Health < REGEN_WAIT_HEALTH_THRESHOLD && self.CanRegenerateHealth(combat.CurrentTimeMs))
        {
            return MovementAction.Stand;
        }

        // Priority 3: Movement based on tactical situation
        // Use stamina for aggressive pushes if opponent is weak
        if (opponent.Health < LOW_HEALTH_THRESHOLD && self.Stamina > 30)
        {
            return MovementAction.Sprint;
        }

        // Default: walk for mobility without exhausting stamina
        if (_random.NextDouble() < 0.3)
        {
            return MovementAction.Walk;
        }

        // Otherwise stand
        return MovementAction.Stand;
    }

    private PostureAction DecidePosture(Operator self, Operator opponent, CombatSystemV2 combat)
    {
        float currentDistance = self.DistanceToOpponent;
        float optimalRange = 15f;

        // Priority 1: Retreat if low health
        if (self.Health < LOW_HEALTH_THRESHOLD)
        {
            return PostureAction.Retreat;
        }

        // Priority 2: Advance if opponent is weak and we're healthy
        if (opponent.Health < LOW_HEALTH_THRESHOLD && self.Health > 60)
        {
            return PostureAction.Advance;
        }

        // Priority 3: Adjust posture based on distance to optimal range
        if (currentDistance > optimalRange + 5)
        {
            // Too far, advance posture
            return PostureAction.Advance;
        }
        else if (currentDistance < optimalRange - 5)
        {
            // Too close, retreat posture
            return PostureAction.Retreat;
        }

        // At optimal range, hold
        return PostureAction.Hold;
    }

    private StanceAction DecideStance(Operator self, Operator opponent, CombatSystemV2 combat)
    {
        // If we're firing and not in ADS, enter ADS for better accuracy
        if (self.CurrentAmmo > 0 && self.WeaponState == WeaponState.Ready)
        {
            // Check current ADS progress
            float adsProgress = self.GetADSProgress(combat.CurrentTimeMs);
            
            // If not in ADS or transitioning, and not actively firing yet, start ADS
            if (adsProgress < 0.5f && !self.IsActivelyFiring)
            {
                return StanceAction.EnterADS;
            }
        }

        // Exit ADS if moving fast or low health (need mobility)
        if (self.MovementState == MovementState.Sprinting || self.Health < 30)
        {
            float adsProgress = self.GetADSProgress(combat.CurrentTimeMs);
            if (adsProgress > 0.1f)
            {
                return StanceAction.ExitADS;
            }
        }

        return StanceAction.None;
    }

    /// <summary>
    /// Decides reaction at a reaction window.
    /// Returns true if the AI wants to change its intent.
    /// </summary>
    public bool ShouldReact(Operator self, Operator opponent, CombatSystemV2 combat, out SimultaneousIntents? newIntents)
    {
        newIntents = null;

        // React if took significant damage recently
        if (self.LastDamageTimeMs.HasValue && 
            combat.CurrentTimeMs - self.LastDamageTimeMs.Value < 200) // Very recent damage
        {
            // Reassess situation
            newIntents = DecideIntents(self, opponent, combat);
            return true;
        }

        // React if ammo empty
        if (self.CurrentAmmo == 0 && self.WeaponState == WeaponState.Ready)
        {
            var intents = new SimultaneousIntents(self.Id);
            intents.Primary = PrimaryAction.Reload;
            newIntents = intents;
            return true;
        }

        // React if opponent finished reloading (opportunity to push)
        if (opponent.WeaponState == WeaponState.Ready &&
            self.DistanceToOpponent > 10 &&
            self.Stamina > 50 &&
            _random.NextDouble() < 0.3)
        {
            var intents = new SimultaneousIntents(self.Id);
            intents.Movement = MovementAction.Sprint;
            intents.Posture = PostureAction.Advance;
            intents.Primary = PrimaryAction.Fire;
            newIntents = intents;
            return true;
        }

        // Otherwise, continue current intent
        return false;
    }
}
