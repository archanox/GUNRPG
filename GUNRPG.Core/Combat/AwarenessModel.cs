using GUNRPG.Core.Operators;

namespace GUNRPG.Core.Combat;

/// <summary>
/// Models awareness and visibility mechanics for operators.
/// Awareness affects what information is available to opponents and AI decision-making.
/// Full cover blocks information, not just damage.
/// </summary>
public static class AwarenessModel
{
    /// <summary>
    /// Base recognition delay in milliseconds when an operator exits full cover.
    /// This is scaled by the observer's accuracy proficiency.
    /// </summary>
    public const float BaseRecognitionDelayMs = 200f;

    /// <summary>
    /// Minimum recognition delay even for highly proficient observers (ms).
    /// Represents human reaction time floor.
    /// </summary>
    public const float MinRecognitionDelayMs = 50f;

    /// <summary>
    /// Maximum recognition delay for low proficiency observers (ms).
    /// </summary>
    public const float MaxRecognitionDelayMs = 350f;

    /// <summary>
    /// Suppression modifier that increases recognition delay.
    /// At full suppression, recognition delay is multiplied by this factor.
    /// </summary>
    public const float MaxSuppressionRecognitionMultiplier = 1.5f;

    /// <summary>
    /// Heavy accuracy penalty applied during recognition delay period.
    /// Shots during this period are essentially blind fire.
    /// </summary>
    public const float RecognitionPenaltyAccuracyMultiplier = 0.3f;

    /// <summary>
    /// Determines if an observer can see a target based on the target's cover state.
    /// </summary>
    /// <param name="targetCoverState">The cover state of the target being observed.</param>
    /// <returns>True if the target is visible, false if fully concealed.</returns>
    public static bool CanSeeTarget(CoverState targetCoverState)
    {
        return targetCoverState switch
        {
            CoverState.None => true,     // Fully visible
            CoverState.Partial => true,  // Partially visible (peeking)
            CoverState.Full => false,    // Not visible (concealed)
            _ => true
        };
    }

    /// <summary>
    /// Gets the visibility level of a target (for graded awareness).
    /// </summary>
    /// <param name="targetCoverState">The cover state of the target.</param>
    /// <returns>
    /// 1.0 = fully visible (None)
    /// 0.5 = partially visible (Partial)
    /// 0.0 = not visible (Full)
    /// </returns>
    public static float GetVisibilityLevel(CoverState targetCoverState)
    {
        return targetCoverState switch
        {
            CoverState.None => 1.0f,
            CoverState.Partial => 0.5f,
            CoverState.Full => 0.0f,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Calculates the recognition delay when a target becomes visible (exits full cover).
    /// Higher accuracy proficiency = faster recognition.
    /// </summary>
    /// <param name="observerAccuracyProficiency">Observer's accuracy proficiency (0.0-1.0)</param>
    /// <param name="observerSuppressionLevel">Observer's current suppression level (0.0-1.0)</param>
    /// <returns>Recognition delay in milliseconds</returns>
    public static float CalculateRecognitionDelayMs(
        float observerAccuracyProficiency,
        float observerSuppressionLevel = 0f)
    {
        observerAccuracyProficiency = Math.Clamp(observerAccuracyProficiency, 0f, 1f);
        observerSuppressionLevel = Math.Clamp(observerSuppressionLevel, 0f, 1f);

        // Base delay scaled inversely by proficiency
        // High proficiency (1.0) = low delay, Low proficiency (0.0) = high delay
        float proficiencyFactor = 1.0f - observerAccuracyProficiency;
        float baseDelay = BaseRecognitionDelayMs * proficiencyFactor;

        // Apply suppression modifier (suppressed observers take longer to recognize)
        float suppressionMultiplier = 1.0f + (observerSuppressionLevel * (MaxSuppressionRecognitionMultiplier - 1.0f));
        float adjustedDelay = baseDelay * suppressionMultiplier;

        // Clamp to bounds
        return Math.Clamp(adjustedDelay, MinRecognitionDelayMs, MaxRecognitionDelayMs);
    }

    /// <summary>
    /// Determines if an operator should fire suppressive fire instead of direct fire.
    /// Suppressive fire is used when the target is not visible but believed to be present.
    /// </summary>
    /// <param name="targetCoverState">Target's current cover state</param>
    /// <param name="targetWasRecentlyVisible">Whether the target was recently visible</param>
    /// <returns>True if suppressive fire should be used</returns>
    public static bool ShouldUseSuppressiveFire(CoverState targetCoverState, bool targetWasRecentlyVisible)
    {
        // Suppressive fire when target is in full cover but was recently seen
        return targetCoverState == CoverState.Full && targetWasRecentlyVisible;
    }

    /// <summary>
    /// Gets the accuracy penalty multiplier during recognition delay.
    /// During recognition, shots are essentially blind fire.
    /// </summary>
    /// <param name="recognitionProgress">Progress through recognition (0.0 = just started, 1.0 = complete)</param>
    /// <returns>Accuracy multiplier (lower = worse accuracy)</returns>
    public static float GetRecognitionAccuracyMultiplier(float recognitionProgress)
    {
        recognitionProgress = Math.Clamp(recognitionProgress, 0f, 1f);

        // Linear interpolation from heavy penalty to no penalty
        return RecognitionPenaltyAccuracyMultiplier + 
               (1.0f - RecognitionPenaltyAccuracyMultiplier) * recognitionProgress;
    }
}
