using GUNRPG.Core.Weapons;

namespace GUNRPG.Core;

/// <summary>
/// Factory for creating pre-configured weapons.
/// All stats based on real weapon data.
/// </summary>
public static class WeaponFactory
{
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
