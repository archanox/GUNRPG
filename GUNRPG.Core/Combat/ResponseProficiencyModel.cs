namespace GUNRPG.Core.Combat;

/// <summary>
/// Models response proficiency effects on action commitment costs.
/// Response proficiency determines how quickly an operator can transition between actions under pressure.
/// 
/// This completes the operator skill triangle:
/// - Reaction Proficiency → how fast actions start (via AccuracyProficiency recognition delays)
/// - Accuracy Proficiency → how well actions perform
/// - Response Proficiency → how fast actions switch
/// 
/// Design goals:
/// - Scale existing timing costs, not introduce new delays
/// - Deterministic (no randomness)
/// - Applied identically to player and AI operators
/// - Visible in timeline and logs
/// </summary>
public static class ResponseProficiencyModel
{
    /// <summary>
    /// Maximum penalty multiplier for low proficiency operators.
    /// At proficiency 0.0, delays are scaled by this factor.
    /// </summary>
    public const float MaxDelayPenaltyMultiplier = 1.3f;

    /// <summary>
    /// Minimum penalty multiplier (bonus) for high proficiency operators.
    /// At proficiency 1.0, delays are scaled by this factor.
    /// </summary>
    public const float MinDelayPenaltyMultiplier = 0.7f;

    /// <summary>
    /// Proficiency level at which delays are at their base value (1.0x multiplier).
    /// Operators at this proficiency experience no bonus or penalty.
    /// </summary>
    public const float NeutralProficiency = 0.5f;

    /// <summary>
    /// Minimum absolute delay in milliseconds after scaling.
    /// Prevents delays from becoming zero or negative.
    /// </summary>
    public const float MinEffectiveDelayMs = 10f;

    /// <summary>
    /// Calculates the effective delay after applying response proficiency scaling.
    /// 
    /// Formula: effectiveDelayMs = baseDelayMs × lerp(maxPenalty, minPenalty, responseProficiency)
    /// 
    /// Examples:
    /// - Low proficiency (0.0) → 1.3× delays (slower transitions)
    /// - Medium proficiency (0.5) → 1.0× delays (neutral)
    /// - High proficiency (1.0) → 0.7× delays (faster transitions)
    /// </summary>
    /// <param name="baseDelayMs">The base delay in milliseconds before proficiency scaling</param>
    /// <param name="responseProficiency">Operator's response proficiency (0.0-1.0)</param>
    /// <returns>The effective delay in milliseconds after proficiency scaling</returns>
    public static float CalculateEffectiveDelay(float baseDelayMs, float responseProficiency)
    {
        responseProficiency = Math.Clamp(responseProficiency, 0f, 1f);
        
        // Linear interpolation from max penalty to min penalty based on proficiency
        float multiplier = MaxDelayPenaltyMultiplier + 
            (MinDelayPenaltyMultiplier - MaxDelayPenaltyMultiplier) * responseProficiency;
        
        float effectiveDelay = baseDelayMs * multiplier;
        
        // Ensure minimum delay to avoid zero or negative values
        return Math.Max(effectiveDelay, MinEffectiveDelayMs);
    }

    /// <summary>
    /// Calculates the effective delay and returns both the result and the multiplier used.
    /// Useful for logging and timeline display.
    /// </summary>
    /// <param name="baseDelayMs">The base delay in milliseconds before proficiency scaling</param>
    /// <param name="responseProficiency">Operator's response proficiency (0.0-1.0)</param>
    /// <returns>A tuple containing (effectiveDelayMs, multiplierUsed)</returns>
    public static (float effectiveDelayMs, float multiplier) CalculateEffectiveDelayWithMultiplier(
        float baseDelayMs, 
        float responseProficiency)
    {
        responseProficiency = Math.Clamp(responseProficiency, 0f, 1f);
        
        float multiplier = MaxDelayPenaltyMultiplier + 
            (MinDelayPenaltyMultiplier - MaxDelayPenaltyMultiplier) * responseProficiency;
        
        float effectiveDelay = Math.Max(baseDelayMs * multiplier, MinEffectiveDelayMs);
        
        return (effectiveDelay, multiplier);
    }

    /// <summary>
    /// Gets the multiplier for a given response proficiency without applying it to a delay.
    /// </summary>
    /// <param name="responseProficiency">Operator's response proficiency (0.0-1.0)</param>
    /// <returns>The delay multiplier (1.3 at 0.0, 1.0 at 0.5, 0.7 at 1.0)</returns>
    public static float GetDelayMultiplier(float responseProficiency)
    {
        responseProficiency = Math.Clamp(responseProficiency, 0f, 1f);
        
        return MaxDelayPenaltyMultiplier + 
            (MinDelayPenaltyMultiplier - MaxDelayPenaltyMultiplier) * responseProficiency;
    }

    /// <summary>
    /// Calculates effective suppression decay rate based on response proficiency.
    /// Higher proficiency = faster decay (recovery from suppression).
    /// </summary>
    /// <param name="baseDecayRate">Base decay rate per second</param>
    /// <param name="responseProficiency">Operator's response proficiency (0.0-1.0)</param>
    /// <returns>Effective decay rate per second</returns>
    public static float CalculateEffectiveSuppressionDecayRate(
        float baseDecayRate, 
        float responseProficiency)
    {
        responseProficiency = Math.Clamp(responseProficiency, 0f, 1f);
        
        // For decay rate, we want higher proficiency = higher rate (faster recovery)
        // Invert the multiplier logic: low proficiency = slower decay, high = faster
        float multiplier = MinDelayPenaltyMultiplier + 
            (MaxDelayPenaltyMultiplier - MinDelayPenaltyMultiplier) * responseProficiency;
        
        return baseDecayRate * multiplier;
    }
}
