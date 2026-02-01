using GUNRPG.Core.Weapons;

namespace GUNRPG.Core;

/// <summary>
/// Factory for creating pre-configured weapons.
/// All stats based on real weapon data.
/// </summary>
public static class WeaponFactory
{
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
            BulletsPerCommitmentUnit = 3,

            // Suppression - LMGs are highly suppressive
            SuppressionFactor = 1.5f
        }.WithSokol545DamageModel();
    }

    private static Weapon WithSokol545DamageModel(this Weapon weapon)
    {
        weapon.DamageRanges.Clear();
        weapon.DamageRanges.Add(new DamageRange(0f, 51f, 32f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 38f, // 38(x1.2) shown in image
                [BodyPart.Neck] = 32f,
                [BodyPart.UpperTorso] = 32f,
                [BodyPart.LowerTorso] = 32f,
                [BodyPart.UpperArm] = 32f,
                [BodyPart.LowerArm] = 32f,
                [BodyPart.UpperLeg] = 32f,
                [BodyPart.LowerLeg] = 32f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(51f, 71f, 31f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 37f, // Approximate based on 1.2x multiplier
                [BodyPart.Neck] = 31f,
                [BodyPart.UpperTorso] = 31f,
                [BodyPart.LowerTorso] = 31f,
                [BodyPart.UpperArm] = 31f,
                [BodyPart.LowerArm] = 31f,
                [BodyPart.UpperLeg] = 31f,
                [BodyPart.LowerLeg] = 31f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(71f, float.PositiveInfinity, 24f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 29f, // Approximate based on 1.2x multiplier
                [BodyPart.Neck] = 24f,
                [BodyPart.UpperTorso] = 24f,
                [BodyPart.LowerTorso] = 24f,
                [BodyPart.UpperArm] = 24f,
                [BodyPart.LowerArm] = 24f,
                [BodyPart.UpperLeg] = 24f,
                [BodyPart.LowerLeg] = 24f,
            }
        });

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
            BulletsPerCommitmentUnit = 4,

            // Suppression - SMGs are less suppressive
            SuppressionFactor = 0.8f
        }.WithSturmwolf45DamageModel();
    }

    private static Weapon WithSturmwolf45DamageModel(this Weapon weapon)
    {
        weapon.DamageRanges.Clear();
        weapon.DamageRanges.Add(new DamageRange(0f, 11f, 30f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 37f, // 37(x1.2) shown in image
                [BodyPart.Neck] = 30f,
                [BodyPart.UpperTorso] = 30f,
                [BodyPart.LowerTorso] = 30f,
                [BodyPart.UpperArm] = 30f,
                [BodyPart.LowerArm] = 30f,
                [BodyPart.UpperLeg] = 30f,
                [BodyPart.LowerLeg] = 30f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(11f, 18f, 23f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 28f, // Approximate based on 1.2x multiplier
                [BodyPart.Neck] = 23f,
                [BodyPart.UpperTorso] = 23f,
                [BodyPart.LowerTorso] = 23f,
                [BodyPart.UpperArm] = 23f,
                [BodyPart.LowerArm] = 23f,
                [BodyPart.UpperLeg] = 23f,
                [BodyPart.LowerLeg] = 23f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(18f, 26f, 19f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 23f, // Approximate based on 1.2x multiplier
                [BodyPart.Neck] = 19f,
                [BodyPart.UpperTorso] = 19f,
                [BodyPart.LowerTorso] = 19f,
                [BodyPart.UpperArm] = 19f,
                [BodyPart.LowerArm] = 19f,
                [BodyPart.UpperLeg] = 19f,
                [BodyPart.LowerLeg] = 19f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(26f, float.PositiveInfinity, 16f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 19f, // Approximate based on 1.2x multiplier
                [BodyPart.Neck] = 16f,
                [BodyPart.UpperTorso] = 16f,
                [BodyPart.LowerTorso] = 16f,
                [BodyPart.UpperArm] = 16f,
                [BodyPart.LowerArm] = 16f,
                [BodyPart.UpperLeg] = 16f,
                [BodyPart.LowerLeg] = 16f,
            }
        });

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
            BulletsPerCommitmentUnit = 3,

            // Suppression - ARs have standard suppression
            SuppressionFactor = 1.0f
        }.WithM15Mod0DamageModel();
    }

    private static Weapon WithM15Mod0DamageModel(this Weapon weapon)
    {
        weapon.DamageRanges.Clear();
        weapon.DamageRanges.Add(new DamageRange(0f, 30.5f, 21f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 27f,
                [BodyPart.Neck] = 21f,
                [BodyPart.UpperTorso] = 21f,
                [BodyPart.LowerTorso] = 21f,
                [BodyPart.UpperArm] = 21f,
                [BodyPart.LowerArm] = 21f,
                [BodyPart.UpperLeg] = 21f,
                [BodyPart.LowerLeg] = 21f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(30.5f, 46.4f, 18f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 23f,
                [BodyPart.Neck] = 18f,
                [BodyPart.UpperTorso] = 18f,
                [BodyPart.LowerTorso] = 18f,
                [BodyPart.UpperArm] = 18f,
                [BodyPart.LowerArm] = 18f,
                [BodyPart.UpperLeg] = 18f,
                [BodyPart.LowerLeg] = 18f,
            }
        });
        weapon.DamageRanges.Add(new DamageRange(46.4f, float.PositiveInfinity, 17f)
        {
            BodyPartDamageOverrides = new Dictionary<BodyPart, float>
            {
                [BodyPart.Head] = 22f,
                [BodyPart.Neck] = 17f,
                [BodyPart.UpperTorso] = 17f,
                [BodyPart.LowerTorso] = 17f,
                [BodyPart.UpperArm] = 17f,
                [BodyPart.LowerArm] = 17f,
                [BodyPart.UpperLeg] = 17f,
                [BodyPart.LowerLeg] = 17f,
            }
        });

        return weapon;
    }
}
