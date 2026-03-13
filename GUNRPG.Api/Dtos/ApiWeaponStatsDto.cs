namespace GUNRPG.Api.Dtos;

/// <summary>
/// Weapon stats DTO returned from GET /weapons.
/// </summary>
public sealed class ApiWeaponStatsDto
{
    public string Name { get; init; } = "";

    // Firepower
    public int RoundsPerMinute { get; init; }
    public float BulletVelocityMetersPerSecond { get; init; }

    // Magazine
    public int MagazineSize { get; init; }
    public int ReloadTimeMs { get; init; }

    // Damage
    public float BaseDamage { get; init; }
    public float HeadshotMultiplier { get; init; }
    public List<ApiWeaponDamageRangeDto> DamageRanges { get; init; } = new();

    // Accuracy
    public float HipfireSpreadDegrees { get; init; }
    public float ADSSpreadDegrees { get; init; }

    // Recoil
    public float VerticalRecoil { get; init; }
    public float HorizontalRecoil { get; init; }
    public float RecoilRecoveryTimeMs { get; init; }

    // Handling
    public int ADSTimeMs { get; init; }
    public float FlinchResistance { get; init; }
    public float SuppressionFactor { get; init; }

    // Mobility
    public float MovementSpeedMetersPerSecond { get; init; }
    public float SprintingMovementSpeedMetersPerSecond { get; init; }
    public float ADSMovementSpeedMetersPerSecond { get; init; }
    public float SprintToFireTimeMs { get; init; }
}

/// <summary>
/// A damage bracket for a weapon (damage varies by distance).
/// </summary>
public sealed class ApiWeaponDamageRangeDto
{
    public float MinMeters { get; init; }
    public float MaxMeters { get; init; }
    public float Damage { get; init; }
    public float HeadDamage { get; init; }
}
