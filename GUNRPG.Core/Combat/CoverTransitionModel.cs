using GUNRPG.Core.Operators;

namespace GUNRPG.Core.Combat;

/// <summary>
/// Models cover transition delays and exposure windows.
/// Cover transitions are not instantaneous - they create vulnerability windows.
/// </summary>
public static class CoverTransitionModel
{
    // Transition delay constants (in milliseconds)
    // These represent realistic times for moving into/out of cover positions

    /// <summary>
    /// Delay for transitioning from no cover to partial cover (80-150ms).
    /// Moving to a peeking position from exposed.
    /// </summary>
    public const int NoneToPartialDelayMs = 100;

    /// <summary>
    /// Delay for transitioning from partial cover to full cover (80-150ms).
    /// Fully concealing oneself from a peeking position.
    /// </summary>
    public const int PartialToFullDelayMs = 100;

    /// <summary>
    /// Delay for transitioning from full cover to partial cover (100-200ms).
    /// Exposing to peek from complete concealment - takes slightly longer.
    /// </summary>
    public const int FullToPartialDelayMs = 150;

    /// <summary>
    /// Delay for transitioning from partial cover to no cover (80-150ms).
    /// Leaving cover entirely from a peeking position.
    /// </summary>
    public const int PartialToNoneDelayMs = 100;

    /// <summary>
    /// Direct transition from full cover to no cover goes through partial first.
    /// Total delay = FullToPartialDelayMs + PartialToNoneDelayMs
    /// </summary>
    public const int FullToNoneDelayMs = FullToPartialDelayMs + PartialToNoneDelayMs;

    /// <summary>
    /// Direct transition from no cover to full cover goes through partial first.
    /// Total delay = NoneToPartialDelayMs + PartialToFullDelayMs
    /// </summary>
    public const int NoneToFullDelayMs = NoneToPartialDelayMs + PartialToFullDelayMs;

    /// <summary>
    /// Gets the transition delay between two cover states.
    /// </summary>
    /// <param name="fromCover">Starting cover state</param>
    /// <param name="toCover">Target cover state</param>
    /// <returns>Transition delay in milliseconds, or 0 if no change</returns>
    public static int GetTransitionDelayMs(CoverState fromCover, CoverState toCover)
    {
        if (fromCover == toCover)
            return 0;

        return (fromCover, toCover) switch
        {
            // Direct adjacent transitions
            (CoverState.None, CoverState.Partial) => NoneToPartialDelayMs,
            (CoverState.Partial, CoverState.Full) => PartialToFullDelayMs,
            (CoverState.Full, CoverState.Partial) => FullToPartialDelayMs,
            (CoverState.Partial, CoverState.None) => PartialToNoneDelayMs,

            // Multi-step transitions (going through intermediate state)
            (CoverState.None, CoverState.Full) => NoneToFullDelayMs,
            (CoverState.Full, CoverState.None) => FullToNoneDelayMs,

            _ => 0
        };
    }

    /// <summary>
    /// Gets the effective cover state during a transition.
    /// During transitions, the operator is treated as being in partial cover
    /// (exposed enough to be hit, but with some protection).
    /// </summary>
    /// <param name="fromCover">Starting cover state</param>
    /// <param name="toCover">Target cover state</param>
    /// <param name="progressMs">Time elapsed in the transition</param>
    /// <param name="totalDurationMs">Total transition duration</param>
    /// <returns>Effective cover state during transition</returns>
    public static CoverState GetEffectiveCoverDuringTransition(
        CoverState fromCover,
        CoverState toCover,
        long progressMs,
        long totalDurationMs)
    {
        if (progressMs >= totalDurationMs)
            return toCover; // Transition complete

        // During transition, treat as partial cover (exposed)
        // This creates the exposure window described in the design
        return CoverState.Partial;
    }

    /// <summary>
    /// Gets the suppression modifier during cover transitions.
    /// Operators are more susceptible to suppression while transitioning.
    /// </summary>
    /// <param name="isTransitioning">Whether currently in a cover transition</param>
    /// <returns>Suppression buildup multiplier (1.0 = normal)</returns>
    public static float GetTransitionSuppressionMultiplier(bool isTransitioning)
    {
        // 30% increased suppression during transitions (more exposed/stressed)
        return isTransitioning ? 1.3f : 1.0f;
    }

    /// <summary>
    /// Determines if a cover transition can be interrupted (cancelled).
    /// </summary>
    /// <param name="progressMs">Time elapsed in the transition</param>
    /// <param name="totalDurationMs">Total transition duration</param>
    /// <returns>True if the transition can be cancelled</returns>
    public static bool CanCancelTransition(long progressMs, long totalDurationMs)
    {
        // Can cancel if less than 50% through the transition
        return progressMs < (totalDurationMs / 2);
    }
}
