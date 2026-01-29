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
            BulletVelocityMetersPerSecond = 880.0f,
            
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
            BulletVelocityMetersPerSecond = 715.0f,
            
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
            BulletVelocityMetersPerSecond = 400f,
            
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

    /// <summary>
    /// Creates a SOKOL 545 light machine gun.
    /// Stats based on the provided in-game attachment screen.
    /// </summary>
    public static Weapon CreateSokol545()
    {
        return new Weapon("SOKOL 545")
        {
            // Firepower
            RoundsPerMinute = 583,
            BulletVelocityMetersPerSecond = 740.0f,

            // Magazine / handling
            MagazineSize = 102,
            ReloadTimeMs = 7333,

            // Damage ranges
            BaseDamage = 32f,
            HeadshotMultiplier = 1.2f,

            // Accuracy
            HipfireSpreadDegrees = 9.50f,
            JumpHipfireSpreadDegrees = 14.50f,
            SlideHipfireSpreadDegrees = 16.50f,
            DiveHipfireSpreadDegrees = 16.50f,
            ADSSpreadDegrees = 1.0f,

            // Recoil
            VerticalRecoil = 0.5627f,
            HorizontalRecoil = 0.1292f,
            RecoilRecoveryTimeMs = 680f,

            // Advanced recoil / handling
            FirstShotRecoilScale = 1.08f,
            RecoilGunKickDegreesPerSecond = 24.92f,
            HorizontalRecoilControlDegreesPerSecond = 12.92f,
            VerticalRecoilControlDegreesPerSecond = 56.27f,
            KickResetSpeedMs = 680,
            AimingIdleSwayDegreesPerSecond = 8.53f,
            AimingIdleSwayDelayMs = 2200,
            FlinchResistance = 0.10f,

            // ADS / mobility
            ADSTimeMs = 420,
            JumpADSTimeMs = 552,

            MovementSpeedMetersPerSecond = 4.5f,
            CrouchMovementSpeedMetersPerSecond = 2.0f,
            SprintingMovementSpeedMetersPerSecond = 6.6f,
            ADSMovementSpeedMetersPerSecond = 2.5f,

            // Movement penalties
            SprintToFireTimeMs = 235f,
            SlideToFireTimeMs = 470f,
            DiveToFireTimeMs = 550f,
            JumpSprintToFireTimeMs = 329f,

            // Commitment
            BulletsPerCommitmentUnit = 3
        }.WithSokol545DamageModel();
    }

    private static Weapon WithSokol545DamageModel(this Weapon weapon)
    {
        weapon.DamageRanges.Clear();
        weapon.DamageRanges.Add(new DamageRange(0f, 51f, 32f));
        weapon.DamageRanges.Add(new DamageRange(51f, 71f, 31f));
        weapon.DamageRanges.Add(new DamageRange(71f, float.PositiveInfinity, 24f));

        weapon.MinDamageRange = 0f;
        weapon.MaxDamageRange = 0f;
        weapon.MinDamageMultiplier = 1f;

        return weapon;
    }

    /// <summary>
    /// Creates a STURMWOLF 45 submachine gun.
    /// Stats based on the provided in-game attachment screen.
    /// </summary>
    public static Weapon CreateSturmwolf45()
    {
        return new Weapon("STURMWOLF 45")
        {
            // Firepower
            RoundsPerMinute = 667,
            BulletVelocityMetersPerSecond = 540.0f,

            // Magazine / handling
            MagazineSize = 32,
            ReloadTimeMs = 2730,

            // Damage ranges
            BaseDamage = 30f,
            HeadshotMultiplier = 1.2f,

            // Accuracy
            HipfireSpreadDegrees = 7.25f,
            JumpHipfireSpreadDegrees = 12.00f,
            SlideHipfireSpreadDegrees = 15.00f,
            DiveHipfireSpreadDegrees = 15.00f,
            ADSSpreadDegrees = 1.0f,

            // Recoil
            VerticalRecoil = 0.4596f,
            HorizontalRecoil = 0.1311f,
            RecoilRecoveryTimeMs = 600f,

            // Advanced recoil / handling
            FirstShotRecoilScale = 1.00f,
            RecoilGunKickDegreesPerSecond = 44.86f,
            HorizontalRecoilControlDegreesPerSecond = 13.11f,
            VerticalRecoilControlDegreesPerSecond = 45.96f,
            KickResetSpeedMs = 600,
            AimingIdleSwayDegreesPerSecond = 0.39f,
            AimingIdleSwayDelayMs = 2200,
            FlinchResistance = 0.12f,

            // ADS / mobility
            ADSTimeMs = 220,
            JumpADSTimeMs = 291,

            MovementSpeedMetersPerSecond = 4.8f,
            CrouchMovementSpeedMetersPerSecond = 2.7f,
            SprintingMovementSpeedMetersPerSecond = 7.1f,
            ADSMovementSpeedMetersPerSecond = 3.4f,

            // Movement penalties
            SprintToFireTimeMs = 150f,
            SlideToFireTimeMs = 340f,
            DiveToFireTimeMs = 420f,
            JumpSprintToFireTimeMs = 210f,

            // Commitment
            BulletsPerCommitmentUnit = 4
        }.WithSturmwolf45DamageModel();
    }

    private static Weapon WithSturmwolf45DamageModel(this Weapon weapon)
    {
        weapon.DamageRanges.Clear();
        weapon.DamageRanges.Add(new DamageRange(0f, 11f, 30f));
        weapon.DamageRanges.Add(new DamageRange(11f, 18f, 23f));
        weapon.DamageRanges.Add(new DamageRange(18f, 26f, 19f));
        weapon.DamageRanges.Add(new DamageRange(26f, float.PositiveInfinity, 16f));

        weapon.MinDamageRange = 0f;
        weapon.MaxDamageRange = 0f;
        weapon.MinDamageMultiplier = 1f;

        return weapon;
    }

    /// <summary>
    /// Creates a M15 MOD 0 assault rifle.
    /// Stats based on the provided in-game attachment screen.
    /// </summary>
    public static Weapon CreateM15Mod0()
    {
        return new Weapon("M15 MOD 0")
        {
            // Firepower
            RoundsPerMinute = 769,
            BulletVelocityMetersPerSecond = 730.0f,

            // Magazine / handling
            MagazineSize = 30,
            ReloadTimeMs = 3000,

            // Damage ranges
            BaseDamage = 21f,
            HeadshotMultiplier = 1.3f,

            // Accuracy
            HipfireSpreadDegrees = 7.95f,
            JumpHipfireSpreadDegrees = 11.95f,
            SlideHipfireSpreadDegrees = 14.95f,
            DiveHipfireSpreadDegrees = 14.95f,
            ADSSpreadDegrees = 1.0f,

            // Recoil
            VerticalRecoil = 0.4667f,
            HorizontalRecoil = 0.1111f,
            RecoilRecoveryTimeMs = 600f,

            // Advanced recoil / handling
            FirstShotRecoilScale = 1.00f,
            RecoilGunKickDegreesPerSecond = 15.50f,
            HorizontalRecoilControlDegreesPerSecond = 11.11f,
            VerticalRecoilControlDegreesPerSecond = 46.67f,
            KickResetSpeedMs = 600,
            AimingIdleSwayDegreesPerSecond = 0.42f,
            AimingIdleSwayDelayMs = 2200,
            FlinchResistance = 0.10f,

            // ADS / mobility
            ADSTimeMs = 250,
            JumpADSTimeMs = 333,

            MovementSpeedMetersPerSecond = 4.7f,
            CrouchMovementSpeedMetersPerSecond = 2.6f,
            SprintingMovementSpeedMetersPerSecond = 6.8f,
            ADSMovementSpeedMetersPerSecond = 2.9f,

            // Movement penalties
            SprintToFireTimeMs = 210f,
            SlideToFireTimeMs = 430f,
            DiveToFireTimeMs = 400f,
            JumpSprintToFireTimeMs = 385f,

            // Commitment
            BulletsPerCommitmentUnit = 3
        }.WithM15Mod0DamageModel();
    }

    private static Weapon WithM15Mod0DamageModel(this Weapon weapon)
    {
        weapon.DamageRanges.Clear();
        weapon.DamageRanges.Add(new DamageRange(0f, 36f, 21f));
        weapon.DamageRanges.Add(new DamageRange(36f, 55f, 18f));
        weapon.DamageRanges.Add(new DamageRange(55f, float.PositiveInfinity, 17f));

        weapon.MinDamageRange = 0f;
        weapon.MaxDamageRange = 0f;
        weapon.MinDamageMultiplier = 1f;

        return weapon;
    }
}
