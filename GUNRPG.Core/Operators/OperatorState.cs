namespace GUNRPG.Core.Operators;

/// <summary>
/// Movement state of an operator.
/// </summary>
public enum MovementState
{
    Stationary,
    Idle, // Alias for Stationary, kept for backward compatibility
    Walking,
    Sprinting,
    Crouching,
    Sliding
}

/// <summary>
/// Aiming state of an operator.
/// </summary>
public enum AimState
{
    Hip,
    ADS,
    TransitioningToADS,
    TransitioningToHip
}

/// <summary>
/// Weapon state of an operator.
/// </summary>
public enum WeaponState
{
    Ready,
    Reloading,
    Jammed
}

/// <summary>
/// Cover state of an operator.
/// </summary>
public enum CoverState
{
    None,
    Partial,
    Full
}

/// <summary>
/// Tactical posture of an operator - represents intent, not spatial movement.
/// Affects suppression, hit probability, and combat pressure.
/// </summary>
public enum TacticalPosture
{
    /// <summary>
    /// Default posture - holding position without tactical pressure.
    /// </summary>
    Hold,
    
    /// <summary>
    /// Aggressive posture - advancing toward opponent, increasing pressure.
    /// Increases risk but applies more pressure to opponent.
    /// </summary>
    Advance,
    
    /// <summary>
    /// Defensive posture - retreating from opponent, reducing pressure.
    /// Decreases risk but applies less pressure to opponent.
    /// </summary>
    Retreat
}
