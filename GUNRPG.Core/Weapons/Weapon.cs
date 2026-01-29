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
    public float BulletVelocityMetersPerSecond { get; set; }
    public int MagazineSize { get; set; }
    public int ReloadTimeMs { get; set; }
    
    // Damage Stats
    public float BaseDamage { get; set; }
    public float HeadshotMultiplier { get; set; }

    // Advanced damage model: range steps + body part multipliers.
    public List<DamageRange> DamageRanges { get; } = new();
    public Dictionary<BodyPart, float> BodyPartDamageMultipliers { get; } = new();

    // Optional: per range step overrides for body-part damage.
    // If a range defines BodyPartDamageOverrides, those values win over BaseDamage * multiplier.
    public float GetDamageAtDistance(float distance, BodyPart bodyPart)
    {
        if (DamageRanges.Count > 0)
        {
            var range = DamageRanges.FirstOrDefault(r => distance >= r.MinMeters && distance < r.MaxMeters)
                        ?? DamageRanges.Where(r => float.IsPositiveInfinity(r.MaxMeters))
                            .OrderByDescending(r => r.MinMeters)
                            .FirstOrDefault();

            if (range != null && range.BodyPartDamageOverrides != null &&
                range.BodyPartDamageOverrides.TryGetValue(bodyPart, out float overrideDamage))
            {
                return overrideDamage;
            }
        }

        float baseDamage = GetDamageAtDistance(distance, isHeadshot: bodyPart == BodyPart.Head);
        if (BodyPartDamageMultipliers.TryGetValue(bodyPart, out float multiplier))
            return baseDamage * multiplier;

        return baseDamage;
    }
    
    // Accuracy Stats
    public float HipfireSpreadDegrees { get; set; }
    public float JumpHipfireSpreadDegrees { get; set; }
    public float SlideHipfireSpreadDegrees { get; set; }
    public float DiveHipfireSpreadDegrees { get; set; }
    public float ADSSpreadDegrees { get; set; }

    // Advanced Recoil / Handling Stats (raw values)
    public float FirstShotRecoilScale { get; set; }
    public float RecoilGunKickDegreesPerSecond { get; set; }
    public float HorizontalRecoilControlDegreesPerSecond { get; set; }
    public float VerticalRecoilControlDegreesPerSecond { get; set; }
    public int KickResetSpeedMs { get; set; }
    public float AimingIdleSwayDegreesPerSecond { get; set; }
    public int AimingIdleSwayDelayMs { get; set; }
    public float FlinchResistance { get; set; }
    
    // Recoil Stats
    public float VerticalRecoil { get; set; }
    public float HorizontalRecoil { get; set; }
    public float RecoilRecoveryTimeMs { get; set; }
    
    // ADS Stats
    public int ADSTimeMs { get; set; } // Time to enter ADS
    public int JumpADSTimeMs { get; set; }
    public float ADSMovementSpeedMultiplier { get; set; } // Movement speed while ADS (0-1)
    
    // Movement Penalties
    public float SprintToFireTimeMs { get; set; } // Time to exit sprint and fire
    public float SlideToFireTimeMs { get; set; }
    public float DiveToFireTimeMs { get; set; }
    public float JumpSprintToFireTimeMs { get; set; }

    // Mobility (raw, meters/second)
    public float MovementSpeedMetersPerSecond { get; set; }
    public float CrouchMovementSpeedMetersPerSecond { get; set; }
    public float SprintingMovementSpeedMetersPerSecond { get; set; }
    public float ADSMovementSpeedMetersPerSecond { get; set; }
    
    // Commitment Unit (bullets per reaction window)
    public int BulletsPerCommitmentUnit { get; set; }

    public Weapon(string name)
    {
        Name = name;
        // Set defaults
        HeadshotMultiplier = 1.5f;
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
    /// When one or more <see cref="DamageRange"/> entries are configured, uses them to determine
    /// damage based on distance brackets; otherwise falls back to <see cref="BaseDamage"/>.
    /// The <see cref="HeadshotMultiplier"/> is applied on top of the chosen base damage when
    /// <paramref name="isHeadshot"/> is <c>true</c>.
    /// </summary>
    public float GetDamageAtDistance(float distance, bool isHeadshot = false)
    {
        float damage = BaseDamage;

        if (DamageRanges.Count > 0)
        {
            var range = DamageRanges.FirstOrDefault(r => distance >= r.MinMeters && distance < r.MaxMeters);
            if (range == null)
            {
                range = DamageRanges.Where(r => float.IsPositiveInfinity(r.MaxMeters))
                    .OrderByDescending(r => r.MinMeters)
                    .FirstOrDefault();
            }

            damage = range?.Damage ?? BaseDamage;
        }
        
        // Apply headshot multiplier
        if (isHeadshot)
        {
            damage *= HeadshotMultiplier;
        }
        
        return damage;
    }
}

public sealed record DamageRange(float MinMeters, float MaxMeters, float Damage)
{
    public IReadOnlyDictionary<BodyPart, float>? BodyPartDamageOverrides { get; init; }
}

public enum BodyPart
{
    LowerLeg,
    UpperLeg,
    LowerArm,
    UpperArm,
    LowerTorso,
    UpperTorso,
    Neck,
    Head,
    Miss  // Indicates shot missed entirely (overshoot or undershoot)
}
