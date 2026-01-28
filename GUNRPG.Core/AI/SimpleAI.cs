using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;

namespace GUNRPG.Core.AI;

/// <summary>
/// Simple AI for enemy decision making.
/// Makes tactical decisions based on operator state and opponent state.
/// </summary>
public class SimpleAI
{
    private readonly Random _random;

    public SimpleAI(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Decides the next intent for the AI operator.
    /// </summary>
    public Intent DecideIntent(Operator self, Operator opponent, CombatSystem combat)
    {
        // Priority 1: Reload if out of ammo
        if (self.CurrentAmmo == 0 && self.WeaponState == WeaponState.Ready)
        {
            return new ReloadIntent(self.Id);
        }

        // Priority 2: Survive - if low health and not regenerating, create distance
        if (self.Health < 30 && !self.CanRegenerateHealth(combat.CurrentTimeMs))
        {
            return self.Stamina > 50
                ? new SprintIntent(self.Id, towardOpponent: false)
                : new WalkIntent(self.Id, towardOpponent: false);
        }

        // Priority 3: If health is low and can regenerate, stop and wait
        if (self.Health < 40 && self.CanRegenerateHealth(combat.CurrentTimeMs))
        {
            return new StopIntent(self.Id);
        }

        // Priority 4: Reload if low on ammo and opponent is far or reloading
        if (self.CurrentAmmo < 5 &&
            self.WeaponState == WeaponState.Ready &&
            (self.DistanceToOpponent > 15 || opponent.WeaponState == WeaponState.Reloading))
        {
            return new ReloadIntent(self.Id);
        }

        // Priority 5: Engage if in good range and have ammo
        if (self.CurrentAmmo > 0 && self.WeaponState == WeaponState.Ready)
        {
            // Optimal range for most weapons: 10-20m
            float optimalRange = 15f;
            
            if (self.DistanceToOpponent > optimalRange + 5)
            {
                // Too far, close distance while firing
                if (self.AimState != AimState.ADS && self.EquippedWeapon != null)
                {
                    return new EnterADSIntent(self.Id);
                }
                return new FireWeaponIntent(self.Id);
            }
            else if (self.DistanceToOpponent < optimalRange - 5)
            {
                // Too close, back up while firing
                return new FireWeaponIntent(self.Id);
            }
            else
            {
                // Good range, ADS and fire
                if (self.AimState != AimState.ADS && self.EquippedWeapon != null)
                {
                    return new EnterADSIntent(self.Id);
                }
                return new FireWeaponIntent(self.Id);
            }
        }

        // Default: adjust position
        float currentDistance = self.DistanceToOpponent;
        if (currentDistance > 20)
        {
            return new WalkIntent(self.Id, towardOpponent: true);
        }
        else if (currentDistance < 10)
        {
            return new WalkIntent(self.Id, towardOpponent: false);
        }

        // Nothing to do, stop
        return new StopIntent(self.Id);
    }

    /// <summary>
    /// Decides reaction at a reaction window.
    /// Returns true if the AI wants to change its intent.
    /// </summary>
    public bool ShouldReact(Operator self, Operator opponent, CombatSystem combat, out Intent? newIntent)
    {
        newIntent = null;

        // React if took significant damage
        if (self.LastDamageTimeMs.HasValue && 
            combat.CurrentTimeMs - self.LastDamageTimeMs.Value < 1000)
        {
            // Just took damage, reassess
            newIntent = DecideIntent(self, opponent, combat);
            return true;
        }

        // React if ammo empty
        if (self.CurrentAmmo == 0)
        {
            newIntent = new ReloadIntent(self.Id);
            return true;
        }

        // React if opponent finished reloading (opportunity to push)
        if (opponent.WeaponState == WeaponState.Ready &&
            self.DistanceToOpponent > 10 &&
            _random.NextDouble() < 0.3)
        {
            // Small chance to push aggressively
            newIntent = new SprintIntent(self.Id, towardOpponent: true);
            return true;
        }

        // Otherwise, continue current intent
        return false;
    }
}
