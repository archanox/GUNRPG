namespace GUNRPG.Core.Combat;

/// <summary>
/// Models operator-driven accuracy effects that are applied AFTER weapon recoil is calculated.
/// Proficiency affects how effectively the operator counteracts recoil and stabilizes aim,
/// without modifying the weapon's base stats.
/// 
/// This class provides testable, isolated logic for proficiency calculations that are used
/// by HitResolution.ResolveShotWithProficiency and Operator.UpdateRegeneration.
/// 
/// Design goals:
/// - Weapon recoil values remain faithful and unchanged
/// - Operator proficiency determines how effectively recoil and gun kick are counteracted
/// - Accuracy proficiency feels like "player skill", not a flat accuracy buff
/// 
/// Note: Deterministic calculation methods are static and don't require a Random instance.
/// Only sampling methods that need randomness require instantiation with a Random.
/// </summary>
public class AccuracyModel
{
    /// <summary>
    /// Maximum recoil control factor (cap at 60% reduction for highly proficient operators).
    /// </summary>
    public const float MaxRecoilControlFactor = 0.6f;

    /// <summary>
    /// Maximum aim error reduction factor at full proficiency (50% reduction).
    /// This represents how much proficiency can reduce the aim error standard deviation.
    /// </summary>
    public const float MaxAimErrorReductionFactor = 0.5f;

    /// <summary>
    /// Maximum variance reduction factor at full proficiency (30% reduction).
    /// This represents how much proficiency reduces recoil variance for more consistent shots.
    /// </summary>
    public const float MaxVarianceReductionFactor = 0.3f;

    /// <summary>
    /// Base aim error scale factor that converts (1 - accuracy) to standard deviation.
    /// </summary>
    public const float BaseAimErrorScale = 0.15f;

    /// <summary>
    /// Base recovery rate multiplier for a completely unskilled operator (proficiency = 0).
    /// Higher proficiency = faster recoil recovery.
    /// </summary>
    public const float BaseRecoveryRateMultiplier = 0.5f;

    /// <summary>
    /// Maximum recovery rate multiplier (at proficiency = 1.0).
    /// </summary>
    public const float MaxRecoveryRateMultiplier = 2.0f;

    private readonly Random _random;

    /// <summary>
    /// Initializes a new AccuracyModel with an injected random source for testability.
    /// Only required when using sampling methods (SampleAimError, SampleGaussian).
    /// </summary>
    /// <param name="random">Random number generator for deterministic testing.</param>
    public AccuracyModel(Random random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>
    /// Calculates the aim error standard deviation based on operator accuracy and proficiency.
    /// Formula: (1 - accuracy) * BaseAimErrorScale * (1 - proficiency * MaxAimErrorReductionFactor)
    /// 
    /// Higher accuracy = lower base error
    /// Higher proficiency = further reduction in error (up to 50% at max proficiency)
    /// </summary>
    /// <param name="operatorAccuracy">Operator accuracy stat (0.0-1.0)</param>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The aim error standard deviation in degrees.</returns>
    public static float CalculateAimErrorStdDev(float operatorAccuracy, float accuracyProficiency)
    {
        operatorAccuracy = Math.Clamp(operatorAccuracy, 0f, 1f);
        accuracyProficiency = Math.Clamp(accuracyProficiency, 0f, 1f);
        
        // Base error from accuracy
        float baseError = (1.0f - operatorAccuracy) * BaseAimErrorScale;
        
        // Proficiency reduces error by up to MaxAimErrorReductionFactor (50%)
        float proficiencyReduction = 1.0f - accuracyProficiency * MaxAimErrorReductionFactor;
        
        return baseError * proficiencyReduction;
    }

    /// <summary>
    /// Calculates the aim error standard deviation based on proficiency only.
    /// This simplified version uses a linear interpolation from BaseAimErrorScale to a minimum value.
    /// 
    /// Note: For production use with both accuracy and proficiency, use the overload that
    /// accepts both parameters.
    /// </summary>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The aim error standard deviation in degrees.</returns>
    public static float CalculateAimErrorStdDev(float accuracyProficiency)
    {
        accuracyProficiency = Math.Clamp(accuracyProficiency, 0f, 1f);
        
        // Linear interpolation from BaseAimErrorScale (at 0) to minimum (at 1)
        // Minimum error at max proficiency is BaseAimErrorScale * (1 - MaxAimErrorReductionFactor) = 0.075
        float minError = BaseAimErrorScale * (1.0f - MaxAimErrorReductionFactor);
        float stdDev = BaseAimErrorScale - (BaseAimErrorScale - minError) * accuracyProficiency;
        return stdDev;
    }

    /// <summary>
    /// Samples an aim acquisition error from a Gaussian distribution.
    /// This error is applied to the initial shot placement.
    /// Requires an AccuracyModel instance with a Random for sampling.
    /// </summary>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The aim error offset in degrees.</returns>
    public float SampleAimError(float accuracyProficiency)
    {
        float stdDev = CalculateAimErrorStdDev(accuracyProficiency);
        return SampleGaussian(0f, stdDev);
    }

    /// <summary>
    /// Samples an aim acquisition error from a Gaussian distribution using both accuracy and proficiency.
    /// Requires an AccuracyModel instance with a Random for sampling.
    /// </summary>
    /// <param name="operatorAccuracy">Operator accuracy stat (0.0-1.0)</param>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The aim error offset in degrees.</returns>
    public float SampleAimError(float operatorAccuracy, float accuracyProficiency)
    {
        float stdDev = CalculateAimErrorStdDev(operatorAccuracy, accuracyProficiency);
        return SampleGaussian(0f, stdDev);
    }

    /// <summary>
    /// Calculates the effective vertical recoil after operator counteraction.
    /// Higher proficiency = more effective recoil counteraction.
    /// 
    /// Formula: effectiveRecoil = weaponRecoil * (1 - proficiency * MaxRecoilControlFactor)
    /// </summary>
    /// <param name="weaponVerticalRecoil">The weapon's raw vertical recoil value (unchanged)</param>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The effective vertical recoil after operator counteraction.</returns>
    public static float CalculateEffectiveRecoil(float weaponVerticalRecoil, float accuracyProficiency)
    {
        accuracyProficiency = Math.Clamp(accuracyProficiency, 0f, 1f);
        
        // effectiveRecoil = weaponRecoil * (1 - proficiency * MaxRecoilControlFactor)
        // At proficiency 0: effectiveRecoil = weaponRecoil * 1.0 (no reduction)
        // At proficiency 1: effectiveRecoil = weaponRecoil * (1 - 0.6) = weaponRecoil * 0.4 (60% reduction)
        float reductionFactor = CalculateRecoilReductionFactor(accuracyProficiency);
        return weaponVerticalRecoil * reductionFactor;
    }

    /// <summary>
    /// Calculates the recoil reduction factor based on proficiency.
    /// At proficiency 0: returns 1.0 (no reduction)
    /// At proficiency 1: returns 0.4 (60% reduction)
    /// </summary>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The recoil reduction factor to multiply against weapon recoil.</returns>
    public static float CalculateRecoilReductionFactor(float accuracyProficiency)
    {
        accuracyProficiency = Math.Clamp(accuracyProficiency, 0f, 1f);
        return 1f - accuracyProficiency * MaxRecoilControlFactor;
    }

    /// <summary>
    /// Calculates the effective variance after proficiency reduction.
    /// At proficiency 1: variance reduced by MaxVarianceReductionFactor (30%)
    /// </summary>
    /// <param name="baseVariance">The base variance before proficiency adjustment</param>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The effective variance after proficiency reduction.</returns>
    public static float CalculateEffectiveVariance(float baseVariance, float accuracyProficiency)
    {
        accuracyProficiency = Math.Clamp(accuracyProficiency, 0f, 1f);
        return baseVariance * (1.0f - accuracyProficiency * MaxVarianceReductionFactor);
    }

    /// <summary>
    /// Calculates the gun kick recovery rate multiplier based on operator proficiency.
    /// Higher proficiency = faster recovery toward baseline aim.
    /// </summary>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>Multiplier to apply to base recoil recovery rate.</returns>
    public static float CalculateRecoveryRateMultiplier(float accuracyProficiency)
    {
        accuracyProficiency = Math.Clamp(accuracyProficiency, 0f, 1f);
        
        // Linear interpolation from BaseRecoveryRateMultiplier (at 0) to MaxRecoveryRateMultiplier (at 1)
        return BaseRecoveryRateMultiplier + (MaxRecoveryRateMultiplier - BaseRecoveryRateMultiplier) * accuracyProficiency;
    }

    /// <summary>
    /// Applies proficiency-based recovery to the current accumulated recoil.
    /// Called during recoil recovery phase to adjust how quickly the operator stabilizes.
    /// </summary>
    /// <param name="currentRecoilY">Current accumulated vertical recoil</param>
    /// <param name="baseRecoveryAmount">The base recovery amount before proficiency adjustment</param>
    /// <param name="accuracyProficiency">Operator accuracy proficiency (0.0-1.0)</param>
    /// <returns>The new recoil value after proficiency-enhanced recovery.</returns>
    public static float ApplyRecovery(float currentRecoilY, float baseRecoveryAmount, float accuracyProficiency)
    {
        float multiplier = CalculateRecoveryRateMultiplier(accuracyProficiency);
        float adjustedRecovery = baseRecoveryAmount * multiplier;
        
        if (currentRecoilY > 0)
            return Math.Max(0, currentRecoilY - adjustedRecovery);
        if (currentRecoilY < 0)
            return Math.Min(0, currentRecoilY + adjustedRecovery);
        
        return currentRecoilY;
    }

    /// <summary>
    /// Samples from a Gaussian (normal) distribution using the Box-Muller transform.
    /// Requires an AccuracyModel instance with a Random for sampling.
    /// </summary>
    /// <param name="mean">The mean of the distribution</param>
    /// <param name="stdDev">The standard deviation of the distribution</param>
    /// <returns>A sample from the Gaussian distribution.</returns>
    public float SampleGaussian(float mean, float stdDev)
    {
        // Box-Muller transform
        double u1 = 1.0 - _random.NextDouble(); // Uniform(0,1]
        double u2 = 1.0 - _random.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * (float)randStdNormal;
    }

    /// <summary>
    /// Static helper for sampling from a Gaussian distribution when you have your own Random instance.
    /// </summary>
    /// <param name="random">Random number generator</param>
    /// <param name="mean">The mean of the distribution</param>
    /// <param name="stdDev">The standard deviation of the distribution</param>
    /// <returns>A sample from the Gaussian distribution.</returns>
    public static float SampleGaussian(Random random, float mean, float stdDev)
    {
        // Box-Muller transform
        double u1 = 1.0 - random.NextDouble(); // Uniform(0,1]
        double u2 = 1.0 - random.NextDouble();
        double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        return mean + stdDev * (float)randStdNormal;
    }
}
