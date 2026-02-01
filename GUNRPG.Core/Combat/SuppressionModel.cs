namespace GUNRPG.Core.Combat;

/// <summary>
/// Models suppression mechanics - the psychological pressure from near-misses and incoming fire
/// that affects operator performance without dealing damage. Suppression is triggered by
/// threatening shots that miss, applies temporary penalties that decay over time, and
/// complements flinch (hit-based) without replacing it.
/// </summary>
public static class SuppressionModel
{
    /// <summary>
    /// Maximum suppression level (capped to prevent stun-lock).
    /// </summary>
    public const float MaxSuppressionLevel = 1.0f;

    /// <summary>
    /// Minimum suppression level (below this, operator is not considered suppressed).
    /// </summary>
    public const float MinSuppressionLevel = 0.0f;

    /// <summary>
    /// Suppression threshold - level at which an operator is considered "suppressed".
    /// </summary>
    public const float SuppressionThreshold = 0.1f;

    /// <summary>
    /// Base suppression amount applied per near-miss.
    /// Scaled by weapon class, fire rate, and distance.
    /// </summary>
    public const float BaseSuppressionPerMiss = 0.15f;

    /// <summary>
    /// Angular deviation threshold (degrees) within which a miss still causes suppression.
    /// Shots that deviate more than this from the target are not threatening enough to suppress.
    /// </summary>
    public const float SuppressionAngleThresholdDegrees = 0.5f;

    /// <summary>
    /// Exponential decay rate per second when not under fire.
    /// Higher values = faster decay.
    /// </summary>
    public const float DecayRatePerSecond = 0.8f;

    /// <summary>
    /// Decay rate multiplier when under continued fire (slowed decay).
    /// </summary>
    public const float DecayRateUnderFireMultiplier = 0.25f;

    /// <summary>
    /// Time window (ms) to consider "under continued fire" for decay slowdown.
    /// </summary>
    public const long ContinuedFireWindowMs = 500;

    // Effect constants - penalties scale linearly with suppression level

    /// <summary>
    /// Maximum ADS time penalty at full suppression (multiplier).
    /// At max suppression, ADS time is increased by this factor.
    /// </summary>
    public const float MaxADSTimePenaltyMultiplier = 0.5f;

    /// <summary>
    /// Maximum accuracy proficiency reduction at full suppression.
    /// Reduces effective accuracy proficiency by this percentage.
    /// </summary>
    public const float MaxAccuracyProficiencyReduction = 0.4f;

    /// <summary>
    /// Maximum recoil control penalty at full suppression.
    /// Reduces effective recoil control by this percentage.
    /// </summary>
    public const float MaxRecoilControlPenalty = 0.3f;

    /// <summary>
    /// Maximum reaction delay at full suppression (ms).
    /// </summary>
    public const float MaxReactionDelayMs = 50f;

    /// <summary>
    /// Minimum proficiency floor when suppressed (prevents total ineffectiveness).
    /// </summary>
    public const float SuppressionProficiencyFloorFactor = 0.4f;

    /// <summary>
    /// Calculates suppression severity based on weapon characteristics and distance.
    /// </summary>
    /// <param name="weaponSuppressionFactor">Weapon-specific suppression factor (LMGs higher than SMGs)</param>
    /// <param name="weaponFireRateRPM">Weapon fire rate in rounds per minute</param>
    /// <param name="distanceMeters">Distance between shooter and target</param>
    /// <param name="angularDeviationDegrees">How close the shot came to hitting (degrees)</param>
    /// <returns>Suppression severity to apply (0.0 - 1.0)</returns>
    public static float CalculateSuppressionSeverity(
        float weaponSuppressionFactor,
        float weaponFireRateRPM,
        float distanceMeters,
        float angularDeviationDegrees)
    {
        // No suppression if shot was too far off
        if (Math.Abs(angularDeviationDegrees) > SuppressionAngleThresholdDegrees)
            return 0f;

        // Base suppression from weapon class
        float baseSuppression = BaseSuppressionPerMiss * weaponSuppressionFactor;

        // Fire rate factor: higher fire rate = more sustained pressure
        // Normalize around 600 RPM as baseline
        float fireRateFactor = Math.Clamp(weaponFireRateRPM / 600f, 0.5f, 2.0f);

        // Distance factor: closer = more suppressive
        // Peak suppression at close range, falls off with distance
        float distanceFactor = CalculateDistanceFactor(distanceMeters);

        // Angular closeness factor: closer misses are more suppressive
        float closenessFactor = 1.0f - (Math.Abs(angularDeviationDegrees) / SuppressionAngleThresholdDegrees);

        float severity = baseSuppression * fireRateFactor * distanceFactor * closenessFactor;

        return Math.Clamp(severity, 0f, MaxSuppressionLevel);
    }

    /// <summary>
    /// Calculates distance factor for suppression.
    /// Closer targets receive more suppression.
    /// </summary>
    private static float CalculateDistanceFactor(float distanceMeters)
    {
        // Full suppression up to 10m, then linear falloff to 50% at 50m, then 25% beyond
        if (distanceMeters <= 10f)
            return 1.0f;
        if (distanceMeters <= 50f)
            return 1.0f - (distanceMeters - 10f) / 80f; // 0.5 at 50m
        return 0.25f; // Minimum floor at long range
    }

    /// <summary>
    /// Applies suppression decay over time.
    /// </summary>
    /// <param name="currentSuppression">Current suppression level</param>
    /// <param name="deltaMs">Time elapsed since last update</param>
    /// <param name="isUnderFire">Whether the operator is currently under continued fire</param>
    /// <returns>New suppression level after decay</returns>
    public static float ApplyDecay(float currentSuppression, long deltaMs, bool isUnderFire)
    {
        if (currentSuppression <= MinSuppressionLevel)
            return MinSuppressionLevel;

        float deltaSeconds = deltaMs / 1000f;
        float effectiveDecayRate = isUnderFire 
            ? DecayRatePerSecond * DecayRateUnderFireMultiplier 
            : DecayRatePerSecond;

        // Exponential decay: S(t) = S0 * e^(-rate * t)
        float decayFactor = (float)Math.Exp(-effectiveDecayRate * deltaSeconds);
        float newSuppression = currentSuppression * decayFactor;

        // Snap to zero if below threshold
        if (newSuppression < SuppressionThreshold * 0.1f)
            return MinSuppressionLevel;

        return Math.Clamp(newSuppression, MinSuppressionLevel, MaxSuppressionLevel);
    }

    /// <summary>
    /// Calculates ADS time penalty due to suppression.
    /// </summary>
    /// <param name="baseADSTimeMs">Base ADS time in milliseconds</param>
    /// <param name="suppressionLevel">Current suppression level (0.0 - 1.0)</param>
    /// <returns>Effective ADS time including penalty</returns>
    public static float CalculateEffectiveADSTime(float baseADSTimeMs, float suppressionLevel)
    {
        suppressionLevel = Math.Clamp(suppressionLevel, MinSuppressionLevel, MaxSuppressionLevel);
        float penalty = suppressionLevel * MaxADSTimePenaltyMultiplier;
        return baseADSTimeMs * (1f + penalty);
    }

    /// <summary>
    /// Calculates effective accuracy proficiency under suppression.
    /// </summary>
    /// <param name="baseAccuracyProficiency">Base accuracy proficiency (0.0 - 1.0)</param>
    /// <param name="suppressionLevel">Current suppression level (0.0 - 1.0)</param>
    /// <returns>Effective accuracy proficiency after suppression penalty</returns>
    public static float CalculateEffectiveAccuracyProficiency(
        float baseAccuracyProficiency,
        float suppressionLevel)
    {
        baseAccuracyProficiency = Math.Clamp(baseAccuracyProficiency, 0f, 1f);
        suppressionLevel = Math.Clamp(suppressionLevel, MinSuppressionLevel, MaxSuppressionLevel);

        // Calculate reduction factor based on suppression
        float reductionFactor = 1f - (suppressionLevel * MaxAccuracyProficiencyReduction);

        // Apply floor to prevent total ineffectiveness
        float minProficiency = baseAccuracyProficiency * SuppressionProficiencyFloorFactor;
        float effectiveProficiency = baseAccuracyProficiency * reductionFactor;

        return Math.Max(effectiveProficiency, minProficiency);
    }

    /// <summary>
    /// Calculates effective recoil control factor under suppression.
    /// </summary>
    /// <param name="baseRecoilControlFactor">Base recoil control factor</param>
    /// <param name="suppressionLevel">Current suppression level (0.0 - 1.0)</param>
    /// <returns>Effective recoil control factor after suppression penalty</returns>
    public static float CalculateEffectiveRecoilControlFactor(
        float baseRecoilControlFactor,
        float suppressionLevel)
    {
        suppressionLevel = Math.Clamp(suppressionLevel, MinSuppressionLevel, MaxSuppressionLevel);
        float penalty = suppressionLevel * MaxRecoilControlPenalty;
        return baseRecoilControlFactor * (1f - penalty);
    }

    /// <summary>
    /// Calculates reaction delay due to suppression.
    /// </summary>
    /// <param name="suppressionLevel">Current suppression level (0.0 - 1.0)</param>
    /// <returns>Reaction delay in milliseconds</returns>
    public static float CalculateReactionDelay(float suppressionLevel)
    {
        suppressionLevel = Math.Clamp(suppressionLevel, MinSuppressionLevel, MaxSuppressionLevel);
        return suppressionLevel * MaxReactionDelayMs;
    }

    /// <summary>
    /// Determines if suppression should be applied based on shot deviation.
    /// </summary>
    /// <param name="angularDeviationDegrees">Deviation from target in degrees</param>
    /// <returns>True if the shot was close enough to cause suppression</returns>
    public static bool ShouldApplySuppression(float angularDeviationDegrees)
    {
        return Math.Abs(angularDeviationDegrees) <= SuppressionAngleThresholdDegrees;
    }

    /// <summary>
    /// Combines existing suppression with new suppression (stacks up to max).
    /// </summary>
    /// <param name="currentSuppression">Current suppression level</param>
    /// <param name="additionalSuppression">New suppression to add</param>
    /// <returns>Combined suppression level (capped at max)</returns>
    public static float CombineSuppression(float currentSuppression, float additionalSuppression)
    {
        return Math.Clamp(
            currentSuppression + additionalSuppression,
            MinSuppressionLevel,
            MaxSuppressionLevel);
    }
}
