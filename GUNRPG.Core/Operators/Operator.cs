using GUNRPG.Core.Weapons;

namespace GUNRPG.Core.Operators;

/// <summary>
/// Represents an operator (player or AI) in the simulation.
/// Maintains all state relevant to combat and decision-making.
/// </summary>
public class Operator
{
    public Guid Id { get; }
    public string Name { get; set; }

    // Physical State
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public float Stamina { get; set; }
    public float MaxStamina { get; set; }
    public float Fatigue { get; set; }
    public float MaxFatigue { get; set; }

    // State Machines
    public MovementState MovementState { get; set; }
    public AimState AimState { get; set; }
    public WeaponState WeaponState { get; set; }

    // Equipment
    public Weapon? EquippedWeapon { get; set; }
    public int CurrentAmmo { get; set; }

    // Position
    public float DistanceToOpponent { get; set; }

    // Timing for actions
    public long? ActionCompletionTimeMs { get; set; }
    
    // Health Regeneration
    public long? LastDamageTimeMs { get; set; }
    public float HealthRegenDelayMs { get; set; }
    public float HealthRegenRate { get; set; } // Health per second

    // Stamina Regeneration
    public float StaminaRegenRate { get; set; } // Stamina per second
    public float SprintStaminaDrainRate { get; set; } // Stamina per second
    public float SlideStaminaCost { get; set; }

    // Movement speeds (meters per second)
    public float WalkSpeed { get; set; }
    public float SprintSpeed { get; set; }
    public float SlideDistance { get; set; }
    public float SlideDurationMs { get; set; }

    // Recoil tracking
    public float CurrentRecoilX { get; set; }
    public float CurrentRecoilY { get; set; }
    public long? RecoilRecoveryStartMs { get; set; }
    public float RecoilRecoveryRate { get; set; } // Per second

    // Commitment tracking for reaction windows
    public int BulletsFiredSinceLastReaction { get; set; }
    public float MetersMovedSinceLastReaction { get; set; }

    public Operator(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
        
        // Default values
        MaxHealth = 100f;
        Health = MaxHealth;
        MaxStamina = 100f;
        Stamina = MaxStamina;
        MaxFatigue = 100f;
        Fatigue = 0f;
        
        MovementState = MovementState.Idle;
        AimState = AimState.Hip;
        WeaponState = WeaponState.Ready;
        
        // Default regeneration values (Call of Duty style)
        HealthRegenDelayMs = 5000f; // 5 seconds
        HealthRegenRate = 40f; // 40 HP per second
        StaminaRegenRate = 20f; // 20 stamina per second
        SprintStaminaDrainRate = 10f; // 10 stamina per second
        SlideStaminaCost = 30f;
        RecoilRecoveryRate = 5f; // Arbitrary units per second
        
        // Movement defaults
        WalkSpeed = 4f; // meters per second
        SprintSpeed = 6f;
        SlideDistance = 3f;
        SlideDurationMs = 500f;
    }

    /// <summary>
    /// Checks if the operator is alive.
    /// </summary>
    public bool IsAlive => Health > 0;

    /// <summary>
    /// Applies damage to the operator.
    /// </summary>
    public void TakeDamage(float damage, long currentTimeMs)
    {
        Health = Math.Max(0, Health - damage);
        LastDamageTimeMs = currentTimeMs;
    }

    /// <summary>
    /// Checks if health regeneration should be active.
    /// </summary>
    public bool CanRegenerateHealth(long currentTimeMs)
    {
        if (!LastDamageTimeMs.HasValue)
            return false;
        
        return (currentTimeMs - LastDamageTimeMs.Value) >= HealthRegenDelayMs;
    }

    /// <summary>
    /// Updates regeneration for a time delta.
    /// </summary>
    public void UpdateRegeneration(long deltaMs, long currentTimeMs)
    {
        float deltaSeconds = deltaMs / 1000f;

        // Health regeneration
        if (Health < MaxHealth && CanRegenerateHealth(currentTimeMs))
        {
            Health = Math.Min(MaxHealth, Health + HealthRegenRate * deltaSeconds);
        }

        // Stamina regeneration (always active when not sprinting)
        if (MovementState != MovementState.Sprinting && Stamina < MaxStamina)
        {
            Stamina = Math.Min(MaxStamina, Stamina + StaminaRegenRate * deltaSeconds);
        }

        // Stamina drain during sprint
        if (MovementState == MovementState.Sprinting)
        {
            Stamina = Math.Max(0, Stamina - SprintStaminaDrainRate * deltaSeconds);
            
            // Auto-exit sprint if stamina depleted
            if (Stamina <= 0)
            {
                MovementState = MovementState.Walking;
            }
        }

        // Recoil recovery
        if (RecoilRecoveryStartMs.HasValue && currentTimeMs >= RecoilRecoveryStartMs.Value)
        {
            float recoveryAmount = RecoilRecoveryRate * deltaSeconds;
            
            if (CurrentRecoilX > 0)
                CurrentRecoilX = Math.Max(0, CurrentRecoilX - recoveryAmount);
            else if (CurrentRecoilX < 0)
                CurrentRecoilX = Math.Min(0, CurrentRecoilX + recoveryAmount);
                
            if (CurrentRecoilY > 0)
                CurrentRecoilY = Math.Max(0, CurrentRecoilY - recoveryAmount);
            else if (CurrentRecoilY < 0)
                CurrentRecoilY = Math.Min(0, CurrentRecoilY + recoveryAmount);
        }
    }
}
