using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;

namespace GUNRPG.Core.Combat;

/// <summary>
/// Models suppressive fire behavior when engaging targets in full cover.
/// Suppressive fire is a controlled burst that applies suppression without attempting hit resolution.
/// This prevents unrealistic mag-dumping into cover and maintains tactical realism.
/// </summary>
public static class SuppressiveFireModel
{
    /// <summary>
    /// Default burst size for suppressive fire (rounds).
    /// Short controlled bursts, not full magazine dumps.
    /// </summary>
    public const int DefaultSuppressiveBurstSize = 3;

    /// <summary>
    /// Minimum burst size for suppressive fire.
    /// </summary>
    public const int MinSuppressiveBurstSize = 2;

    /// <summary>
    /// Maximum burst size for suppressive fire.
    /// </summary>
    public const int MaxSuppressiveBurstSize = 6;

    /// <summary>
    /// Base suppression amount applied per suppressive fire burst.
    /// Higher than individual shot suppression due to sustained fire intent.
    /// </summary>
    public const float BaseSuppressiveFireSuppression = 0.25f;

    /// <summary>
    /// Multiplier for suppression when target is in full cover.
    /// Full cover amplifies psychological pressure even if no damage is possible.
    /// </summary>
    public const float FullCoverSuppressionMultiplier = 0.8f;

    /// <summary>
    /// Time window (ms) to remember that a target was recently visible.
    /// After this time, AI should not continue suppressive fire.
    /// </summary>
    public const long TargetLastSeenWindowMs = 3000;

    /// <summary>
    /// Cooldown after suppressive fire before next action (ms).
    /// Represents tactical pause to assess effect.
    /// </summary>
    public const int PostSuppressiveCooldownMs = 200;

    /// <summary>
    /// Calculates the burst size for suppressive fire based on weapon and situation.
    /// </summary>
    /// <param name="weapon">The weapon being fired</param>
    /// <param name="availableAmmo">Current ammo count</param>
    /// <returns>Number of rounds to fire in suppressive burst</returns>
    public static int CalculateSuppressiveBurstSize(Weapon weapon, int availableAmmo)
    {
        // Base burst size
        int burstSize = DefaultSuppressiveBurstSize;

        // LMGs and weapons with high suppression factors get slightly larger bursts
        if (weapon.SuppressionFactor >= 1.3f)
        {
            burstSize = Math.Min(MaxSuppressiveBurstSize, burstSize + 2);
        }
        else if (weapon.SuppressionFactor >= 1.0f)
        {
            burstSize = Math.Min(MaxSuppressiveBurstSize, burstSize + 1);
        }

        // Don't exceed available ammo
        burstSize = Math.Min(burstSize, availableAmmo);

        // Ensure minimum burst
        return Math.Max(MinSuppressiveBurstSize, burstSize);
    }

    /// <summary>
    /// Calculates suppression severity from a suppressive fire burst.
    /// </summary>
    /// <param name="weapon">Weapon used for suppressive fire</param>
    /// <param name="burstSize">Number of rounds in the burst</param>
    /// <param name="distanceMeters">Distance to target</param>
    /// <param name="targetMovementState">Target's movement state (optional)</param>
    /// <returns>Total suppression severity to apply</returns>
    public static float CalculateSuppressiveBurstSeverity(
        Weapon weapon,
        int burstSize,
        float distanceMeters,
        MovementState? targetMovementState = null)
    {
        // Base suppression from burst
        float baseSuppression = BaseSuppressiveFireSuppression;

        // Scale by weapon suppression factor
        baseSuppression *= weapon.SuppressionFactor;

        // Scale by burst size (diminishing returns)
        float burstMultiplier = 1.0f + (burstSize - 1) * 0.2f; // Each additional round adds 20%
        baseSuppression *= burstMultiplier;

        // Distance factor (closer = more suppressive)
        float distanceFactor = CalculateDistanceFactor(distanceMeters);
        baseSuppression *= distanceFactor;

        // Apply full cover multiplier (reduced effectiveness since not visible)
        baseSuppression *= FullCoverSuppressionMultiplier;

        // Apply target movement modifier
        if (targetMovementState.HasValue)
        {
            float movementMultiplier = MovementModel.GetSuppressionBuildupMultiplier(targetMovementState.Value);
            baseSuppression *= movementMultiplier;
        }

        return Math.Clamp(baseSuppression, 0f, SuppressionModel.MaxSuppressionLevel);
    }

    /// <summary>
    /// Calculates the duration of a suppressive fire burst in milliseconds.
    /// </summary>
    /// <param name="weapon">Weapon being fired</param>
    /// <param name="burstSize">Number of rounds in burst</param>
    /// <returns>Duration in milliseconds</returns>
    public static long CalculateBurstDurationMs(Weapon weapon, int burstSize)
    {
        float timeBetweenShots = weapon.GetTimeBetweenShotsMs();
        // Duration = time for (burstSize - 1) intervals between shots
        return (long)((burstSize - 1) * timeBetweenShots);
    }

    /// <summary>
    /// Determines if suppressive fire should be used given the combat situation.
    /// </summary>
    /// <param name="attackerAmmo">Attacker's current ammo</param>
    /// <param name="targetCoverState">Target's cover state</param>
    /// <param name="targetLastVisibleMs">When the target was last visible (null if never seen)</param>
    /// <param name="currentTimeMs">Current simulation time</param>
    /// <returns>True if suppressive fire should be used</returns>
    public static bool ShouldUseSuppressiveFire(
        int attackerAmmo,
        CoverState targetCoverState,
        long? targetLastVisibleMs,
        long currentTimeMs)
    {
        // Must have ammo
        if (attackerAmmo < MinSuppressiveBurstSize)
            return false;

        // Target must be in full cover
        if (targetCoverState != CoverState.Full)
            return false;

        // Target must have been recently visible (we know they're there)
        if (!targetLastVisibleMs.HasValue)
            return false;

        // Check if target was seen within the memory window
        long timeSinceVisible = currentTimeMs - targetLastVisibleMs.Value;
        return timeSinceVisible <= TargetLastSeenWindowMs;
    }

    /// <summary>
    /// Distance factor for suppressive fire (closer = more effective).
    /// Similar to SuppressionModel but with different curve for sustained fire.
    /// </summary>
    private static float CalculateDistanceFactor(float distanceMeters)
    {
        // Full suppression up to 15m, then linear falloff
        if (distanceMeters <= 15f)
            return 1.0f;
        if (distanceMeters <= 40f)
            return 1.0f - (distanceMeters - 15f) / 50f; // 0.5 at 40m
        return 0.3f; // Minimum floor at long range
    }
}
