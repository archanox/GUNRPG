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

    // Operator Skills
    private float _accuracy = 0.7f;
    /// <summary>
    /// Operator accuracy (0.0 to 1.0). Affects standard deviation of aim acquisition error.
    /// Higher values = more accurate shooting. Values are clamped to [0.0, 1.0] range.
    /// </summary>
    public float Accuracy 
    { 
        get => _accuracy;
        set => _accuracy = Math.Clamp(value, 0.0f, 1.0f);
    }

    private float _accuracyProficiency = 0.5f;
    /// <summary>
    /// Operator accuracy proficiency (0.0 to 1.0). Determines how effectively the operator
    /// counteracts recoil and stabilizes aim. This is applied AFTER weapon recoil is calculated.
    /// 
    /// - 0.0 = no recoil control, poor aim stabilization
    /// - 1.0 = excellent recoil control and fast recovery (capped well below perfect)
    /// 
    /// Affects:
    /// 1. Initial aim acquisition error (higher proficiency = tighter distribution)
    /// 2. Recoil counteraction (reduces effective vertical recoil per shot)
    /// 3. Gun kick recovery (faster recovery toward baseline aim)
    /// 
    /// Does NOT affect damage, fire rate, or body-part bands.
    /// </summary>
    public float AccuracyProficiency
    {
        get => _accuracyProficiency;
        set => _accuracyProficiency = Math.Clamp(value, 0.0f, 1.0f);
    }

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

    // ADS Transition tracking
    public long? ADSTransitionStartMs { get; set; }
    public float ADSTransitionDurationMs { get; set; }
    public bool IsActivelyFiring { get; set; } // Track if currently in firing burst

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
        
        // Operator skills (using property setter for validation)
        Accuracy = 0.7f; // Default 70% accuracy
        AccuracyProficiency = 0.5f; // Default 50% proficiency (mid-range skill)
        
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

        // Recoil recovery (affected by AccuracyProficiency)
        if (RecoilRecoveryStartMs.HasValue && currentTimeMs >= RecoilRecoveryStartMs.Value)
        {
            // AccuracyProficiency affects gun kick recovery rate
            // At proficiency 0.0: recovery multiplier = 0.5 (slower recovery)
            // At proficiency 1.0: recovery multiplier = 2.0 (faster recovery)
            float recoveryMultiplier = 0.5f + 1.5f * AccuracyProficiency;
            float recoveryAmount = RecoilRecoveryRate * deltaSeconds * recoveryMultiplier;
            CurrentRecoilX = RecoverRecoilAxis(CurrentRecoilX, recoveryAmount);
            CurrentRecoilY = RecoverRecoilAxis(CurrentRecoilY, recoveryAmount);
        }
    }

    private static float RecoverRecoilAxis(float recoilValue, float recoveryAmount)
    {
        if (recoilValue > 0)
            return Math.Max(0, recoilValue - recoveryAmount);
        if (recoilValue < 0)
            return Math.Min(0, recoilValue + recoveryAmount);
        return recoilValue;
    }

    /// <summary>
    /// Gets the ADS transition progress (0.0 = hip, 1.0 = full ADS).
    /// </summary>
    public float GetADSProgress(long currentTimeMs)
    {
        if (AimState == AimState.Hip)
            return 0f;
        
        if (AimState == AimState.ADS)
            return 1f;

        if (AimState == AimState.TransitioningToADS && ADSTransitionStartMs.HasValue)
        {
            float elapsed = currentTimeMs - ADSTransitionStartMs.Value;
            float duration = ADSTransitionDurationMs <= 0f ? 1f : ADSTransitionDurationMs;
            float progress = Math.Clamp(elapsed / duration, 0f, 1f);
            return progress;
        }

        if (AimState == AimState.TransitioningToHip && ADSTransitionStartMs.HasValue)
        {
            float elapsed = currentTimeMs - ADSTransitionStartMs.Value;
            float duration = ADSTransitionDurationMs <= 0f ? 1f : ADSTransitionDurationMs;
            float progress = 1f - Math.Clamp(elapsed / duration, 0f, 1f);
            return progress;
        }

        return 0f;
    }

    /// <summary>
    /// Gets the current weapon spread based on ADS progress.
    /// Interpolates between hipfire and ADS spread.
    /// </summary>
    public float GetCurrentSpread(long currentTimeMs)
    {
        if (EquippedWeapon == null)
            return 10f; // Default high spread if no weapon

        float adsProgress = GetADSProgress(currentTimeMs);
        float hipSpread = EquippedWeapon.HipfireSpreadDegrees;
        float adsSpread = EquippedWeapon.ADSSpreadDegrees;

        // Linear interpolation between hip and ADS spread
        return hipSpread + (adsSpread - hipSpread) * adsProgress;
    }
}
