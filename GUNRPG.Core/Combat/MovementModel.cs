using GUNRPG.Core.Operators;

namespace GUNRPG.Core.Combat;

/// <summary>
/// Models operator movement mechanics and their impact on combat effectiveness.
/// Movement affects accuracy, weapon sway, ADS time, and suppression without requiring spatial coordinates.
/// </summary>
public static class MovementModel
{
    // Accuracy Multipliers (applied to base accuracy)
    public const float StationaryAccuracyMultiplier = 1.0f;
    public const float WalkingAccuracyMultiplier = 0.85f;
    public const float SprintingAccuracyMultiplier = 0.45f;
    public const float CrouchingAccuracyMultiplier = 1.1f;

    // Weapon Sway (angular noise in degrees, layered on top of recoil)
    public const float StationarySwayDegrees = 0.0f;
    public const float WalkingSwayDegrees = 0.05f;
    public const float SprintingSwayDegrees = 0.15f;
    public const float CrouchingSwayDegrees = 0.02f;

    // ADS Time Multipliers
    public const float StationaryADSMultiplier = 1.0f;
    public const float WalkingADSMultiplier = 1.2f;
    public const float SprintingADSMultiplier = 1.6f;
    public const float CrouchingADSMultiplier = 0.9f;

    // Suppression Interaction
    public const float WalkingSuppressionBuildupMultiplier = 1.15f;
    public const float SprintingSuppressionBuildupMultiplier = 1.3f;
    public const float CrouchingSuppressionReduction = 0.2f; // 20% reduction
    public const float CrouchingSuppressionDecayMultiplier = 1.4f; // 40% faster decay

    // Tactical Posture Modifiers
    public const float AdvanceSuppressionBuildupMultiplier = 1.2f; // Advancing = more exposed
    public const float RetreatSuppressionBuildupMultiplier = 0.85f; // Retreating = less exposed
    public const float AdvanceHitProbabilityMultiplier = 1.15f; // Advancing = easier to hit
    public const float RetreatHitProbabilityMultiplier = 0.9f; // Retreating = harder to hit

    // Cover Modifiers
    public const float PartialCoverHitProbabilityMultiplier = 0.7f; // 30% reduction in hit chance
    public const float FullCoverHitProbabilityMultiplier = 0.0f; // Blocks all hits when not peeking

    /// <summary>
    /// Gets the accuracy multiplier for the given movement state.
    /// </summary>
    public static float GetAccuracyMultiplier(MovementState movement)
    {
        return movement switch
        {
            MovementState.Stationary => StationaryAccuracyMultiplier,
            MovementState.Idle => StationaryAccuracyMultiplier,
            MovementState.Walking => WalkingAccuracyMultiplier,
            MovementState.Sprinting => SprintingAccuracyMultiplier,
            MovementState.Crouching => CrouchingAccuracyMultiplier,
            MovementState.Sliding => SprintingAccuracyMultiplier, // Same penalty as sprinting
            _ => StationaryAccuracyMultiplier
        };
    }

    /// <summary>
    /// Gets the weapon sway in degrees for the given movement state.
    /// Sway is additional angular noise layered on top of recoil.
    /// </summary>
    public static float GetWeaponSwayDegrees(MovementState movement)
    {
        return movement switch
        {
            MovementState.Stationary => StationarySwayDegrees,
            MovementState.Idle => StationarySwayDegrees,
            MovementState.Walking => WalkingSwayDegrees,
            MovementState.Sprinting => SprintingSwayDegrees,
            MovementState.Crouching => CrouchingSwayDegrees,
            MovementState.Sliding => SprintingSwayDegrees,
            _ => StationarySwayDegrees
        };
    }

    /// <summary>
    /// Gets the ADS time multiplier for the given movement state.
    /// </summary>
    public static float GetADSTimeMultiplier(MovementState movement)
    {
        return movement switch
        {
            MovementState.Stationary => StationaryADSMultiplier,
            MovementState.Idle => StationaryADSMultiplier,
            MovementState.Walking => WalkingADSMultiplier,
            MovementState.Sprinting => SprintingADSMultiplier,
            MovementState.Crouching => CrouchingADSMultiplier,
            MovementState.Sliding => SprintingADSMultiplier,
            _ => StationaryADSMultiplier
        };
    }

    /// <summary>
    /// Gets the suppression buildup multiplier for the given movement state.
    /// Higher values mean the operator is more susceptible to suppression while moving.
    /// </summary>
    public static float GetSuppressionBuildupMultiplier(MovementState movement)
    {
        return movement switch
        {
            MovementState.Stationary => 1.0f,
            MovementState.Idle => 1.0f,
            MovementState.Walking => WalkingSuppressionBuildupMultiplier,
            MovementState.Sprinting => SprintingSuppressionBuildupMultiplier,
            MovementState.Crouching => 1.0f - CrouchingSuppressionReduction,
            MovementState.Sliding => SprintingSuppressionBuildupMultiplier,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Gets the suppression decay multiplier for the given movement state.
    /// Higher values mean faster decay (recovery from suppression).
    /// </summary>
    public static float GetSuppressionDecayMultiplier(MovementState movement)
    {
        return movement switch
        {
            MovementState.Crouching => CrouchingSuppressionDecayMultiplier,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Gets the hit probability multiplier for the given cover state.
    /// This is applied to incoming shots to reduce hit chance.
    /// </summary>
    public static float GetCoverHitProbabilityMultiplier(CoverState cover, bool isPeeking = false)
    {
        if (cover == CoverState.Full && !isPeeking)
        {
            return FullCoverHitProbabilityMultiplier; // Full cover blocks hits
        }
        
        return cover switch
        {
            CoverState.None => 1.0f,
            CoverState.Partial => PartialCoverHitProbabilityMultiplier,
            CoverState.Full => 1.0f, // When peeking, full cover acts like no cover
            _ => 1.0f
        };
    }

    /// <summary>
    /// Checks if the operator can enter cover based on movement state.
    /// Cover can only be entered when stationary or crouching.
    /// </summary>
    public static bool CanEnterCover(MovementState movement)
    {
        return movement == MovementState.Stationary 
            || movement == MovementState.Idle 
            || movement == MovementState.Crouching;
    }

    /// <summary>
    /// Gets the suppression buildup multiplier for tactical posture.
    /// Advancing = more exposed to suppression, Retreating = less exposed.
    /// </summary>
    public static float GetTacticalPostureSuppressionMultiplier(TacticalPosture posture)
    {
        return posture switch
        {
            TacticalPosture.Advance => AdvanceSuppressionBuildupMultiplier,
            TacticalPosture.Retreat => RetreatSuppressionBuildupMultiplier,
            TacticalPosture.Hold => 1.0f,
            _ => 1.0f
        };
    }

    /// <summary>
    /// Gets the hit probability multiplier for tactical posture.
    /// Advancing = easier to hit, Retreating = harder to hit.
    /// </summary>
    public static float GetTacticalPostureHitProbabilityMultiplier(TacticalPosture posture)
    {
        return posture switch
        {
            TacticalPosture.Advance => AdvanceHitProbabilityMultiplier,
            TacticalPosture.Retreat => RetreatHitProbabilityMultiplier,
            TacticalPosture.Hold => 1.0f,
            _ => 1.0f
        };
    }
}
