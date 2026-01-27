using GUNRPG.Core.Weapons;

namespace GUNRPG.Core;

/// <summary>
/// Factory for creating pre-configured weapons.
/// All stats based on real weapon data.
/// </summary>
public static class WeaponFactory
{
    /// <summary>
    /// Creates an RK-9 submachine gun.
    /// Stats based on the provided in-game attachment screen (burst ignored).
    /// </summary>
    public static Weapon CreateRK9()
    {
        return new Weapon("RK-9")
        {
            // Firepower
            RoundsPerMinute = 709,
            BulletVelocityMetersPerSecond = 570.0f,

            // Magazine / handling
            MagazineSize = 30,
            ReloadTimeMs = 2867,

            // Damage ranges (Damage Range table)
            BaseDamage = 32f,
            HeadshotMultiplier = 203f / 32f,

            // Accuracy
            HipfireSpreadDegrees = 7.25f,
            JumpHipfireSpreadDegrees = 12.00f,
            SlideHipfireSpreadDegrees = 13.50f,
            DiveHipfireSpreadDegrees = 13.50f,

            // Not shown in the image as a single value; keep existing model's ADS spread conservative.
            ADSSpreadDegrees = 1.0f,

            // Recoil (existing simple recoil model)
            VerticalRecoil = 0.3875f,
            HorizontalRecoil = 0.1985f,
            RecoilRecoveryTimeMs = 600f,

            // Advanced recoil / handling
            FirstShotRecoilScale = 1.08f,
            RecoilGunKickDegreesPerSecond = 40.95f,
            HorizontalRecoilControlDegreesPerSecond = 19.85f,
            VerticalRecoilControlDegreesPerSecond = 38.75f,
            KickResetSpeedMs = 600,
            AimingIdleSwayDegreesPerSecond = 0.39f,
            AimingIdleSwayDelayMs = 2200,
            FlinchResistance = 0.07f,

            // ADS / mobility
            ADSTimeMs = 200,
            JumpADSTimeMs = 263,
            ADSMovementSpeedMultiplier = 0.6f,

            MovementSpeedMetersPerSecond = 5.2f,
            CrouchMovementSpeedMetersPerSecond = 2.9f,
            SprintingMovementSpeedMetersPerSecond = 7.3f,
            ADSMovementSpeedMetersPerSecond = 3.6f,

            // Movement penalties
            SprintToFireTimeMs = 132f,
            SlideToFireTimeMs = 320f,
            DiveToFireTimeMs = 400f,
            JumpSprintToFireTimeMs = 211f,

            // Commitment
            BulletsPerCommitmentUnit = 3
        }.WithRK9DamageModel();
    }

    private static Weapon WithRK9DamageModel(this Weapon weapon)
    {
        weapon.DamageRanges.Clear();
        weapon.DamageRanges.Add(new DamageRange(0f, 7f, 32f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 203f,
                [BodyPart.Neck] = 34f,
                [BodyPart.UpperTorso] = 34f,
                [BodyPart.LowerTorso] = 32f,
                [BodyPart.UpperArm] = 32f,
                [BodyPart.LowerArm] = 32f,
                [BodyPart.UpperLeg] = 32f,
                [BodyPart.LowerLeg] = 32f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(7f, 16f, 30f));
        weapon.DamageRanges.Add(new DamageRange(16f, 22f, 23f));
        weapon.DamageRanges.Add(new DamageRange(22f, float.PositiveInfinity, 18f));

        weapon.MinDamageRange = 0f;
        weapon.MaxDamageRange = 0f;
        weapon.MinDamageMultiplier = 1f;

        return weapon;
    }

    /// <summary>
    /// Creates an M4A1 assault rifle.
    /// Stats based on Call of Duty weapon data.
    /// </summary>
    public static Weapon CreateM4A1()
    {
        return new Weapon("M4A1")
        {
            // Fire rate
            RoundsPerMinute = 833, // ~72ms per shot
            BulletVelocityMetersPerSecond = 0f,
            
            // Magazine
            MagazineSize = 30,
            ReloadTimeMs = 2100,
            
            // Damage
            BaseDamage = 28f,
            HeadshotMultiplier = 1.5f,
            
            // Damage falloff
            MinDamageRange = 20f, // meters
            MaxDamageRange = 40f, // meters
            MinDamageMultiplier = 0.7f,
            
            // Accuracy
            HipfireSpreadDegrees = 4.5f,
            ADSSpreadDegrees = 0.8f,
            
            // Recoil
            VerticalRecoil = 0.4f,
            HorizontalRecoil = 0.2f,
            RecoilRecoveryTimeMs = 150f,
            
            // ADS
            ADSTimeMs = 250,
            ADSMovementSpeedMultiplier = 0.65f,
            
            // Movement
            SprintToFireTimeMs = 180f,
            
            // Commitment
            BulletsPerCommitmentUnit = 3
        };
    }

    /// <summary>
    /// Creates an AK-47 assault rifle.
    /// Higher damage, slower fire rate, more recoil.
    /// </summary>
    public static Weapon CreateAK47()
    {
        return new Weapon("AK-47")
        {
            RoundsPerMinute = 600, // ~100ms per shot
            BulletVelocityMetersPerSecond = 0f,
            
            MagazineSize = 30,
            ReloadTimeMs = 2400,
            
            BaseDamage = 35f,
            HeadshotMultiplier = 1.5f,
            
            MinDamageRange = 25f,
            MaxDamageRange = 50f,
            MinDamageMultiplier = 0.75f,
            
            HipfireSpreadDegrees = 5.2f,
            ADSSpreadDegrees = 1.2f,
            
            VerticalRecoil = 0.6f,
            HorizontalRecoil = 0.4f,
            RecoilRecoveryTimeMs = 200f,
            
            ADSTimeMs = 280,
            ADSMovementSpeedMultiplier = 0.6f,
            
            SprintToFireTimeMs = 200f,
            
            BulletsPerCommitmentUnit = 3
        };
    }

    /// <summary>
    /// Creates an MP5 submachine gun.
    /// High fire rate, low damage, minimal recoil.
    /// </summary>
    public static Weapon CreateMP5()
    {
        return new Weapon("MP5")
        {
            RoundsPerMinute = 857, // ~70ms per shot
            BulletVelocityMetersPerSecond = 0f,
            
            MagazineSize = 30,
            ReloadTimeMs = 1800,
            
            BaseDamage = 22f,
            HeadshotMultiplier = 1.4f,
            
            MinDamageRange = 12f,
            MaxDamageRange = 25f,
            MinDamageMultiplier = 0.6f,
            
            HipfireSpreadDegrees = 3.8f,
            ADSSpreadDegrees = 0.6f,
            
            VerticalRecoil = 0.25f,
            HorizontalRecoil = 0.15f,
            RecoilRecoveryTimeMs = 100f,
            
            ADSTimeMs = 200,
            ADSMovementSpeedMultiplier = 0.75f,
            
            SprintToFireTimeMs = 150f,
            
            BulletsPerCommitmentUnit = 5 // More bullets per reaction due to high ROF
        };
    }
}
