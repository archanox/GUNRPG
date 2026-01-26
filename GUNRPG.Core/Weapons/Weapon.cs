namespace GUNRPG.Core.Weapons;

/// <summary>
/// Represents weapon configuration with raw stats.
/// All stats are used 1:1 without abstraction.
/// </summary>
public class Weapon
{
    public string Name { get; set; }
    
    // Firing Stats (from raw weapon data)
    public int RoundsPerMinute { get; set; } // Fire rate
    public int MagazineSize { get; set; }
    public int ReloadTimeMs { get; set; }
    
    // Damage Stats
    public float BaseDamage { get; set; }
    public float HeadshotMultiplier { get; set; }
    
    // Damage Falloff (distance in meters -> damage multiplier)
    public float MinDamageRange { get; set; } // Range where damage starts falling off
    public float MaxDamageRange { get; set; } // Range where damage is at minimum
    public float MinDamageMultiplier { get; set; } // Minimum damage multiplier at max range
    
    // Accuracy Stats
    public float HipfireSpreadDegrees { get; set; }
    public float ADSSpreadDegrees { get; set; }
    
    // Recoil Stats
    public float VerticalRecoil { get; set; }
    public float HorizontalRecoil { get; set; }
    public float RecoilRecoveryTimeMs { get; set; }
    
    // ADS Stats
    public int ADSTimeMs { get; set; } // Time to enter ADS
    public float ADSMovementSpeedMultiplier { get; set; } // Movement speed while ADS (0-1)
    
    // Movement Penalties
    public float SprintToFireTimeMs { get; set; } // Time to exit sprint and fire
    
    // Commitment Unit (bullets per reaction window)
    public int BulletsPerCommitmentUnit { get; set; }

    public Weapon(string name)
    {
        Name = name;
        // Set defaults
        HeadshotMultiplier = 1.5f;
        MinDamageMultiplier = 0.7f;
        ADSMovementSpeedMultiplier = 0.6f;
        BulletsPerCommitmentUnit = 3; // Default: reaction every 3 bullets
    }

    /// <summary>
    /// Calculates time between shots in milliseconds.
    /// </summary>
    public float GetTimeBetweenShotsMs()
    {
        return 60000f / RoundsPerMinute;
    }

    /// <summary>
    /// Calculates damage at a given distance.
    /// </summary>
    public float GetDamageAtDistance(float distance, bool isHeadshot = false)
    {
        float damage = BaseDamage;
        
        // Apply distance falloff
        if (distance > MinDamageRange)
        {
            float falloffProgress = (distance - MinDamageRange) / (MaxDamageRange - MinDamageRange);
            falloffProgress = Math.Clamp(falloffProgress, 0f, 1f);
            float damageMultiplier = 1f - (falloffProgress * (1f - MinDamageMultiplier));
            damage *= damageMultiplier;
        }
        
        // Apply headshot multiplier
        if (isHeadshot)
        {
            damage *= HeadshotMultiplier;
        }
        
        return damage;
    }
}
