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
/// Movement direction relative to opponent.
/// Affects suppression buildup.
/// </summary>
public enum MovementDirection
{
    /// <summary>
    /// Not moving or movement direction is neutral.
    /// </summary>
    Holding,
    
    /// <summary>
    /// Moving toward the opponent (closing distance).
    /// Increases suppression buildup (more aggressive/exposed).
    /// </summary>
    Advancing,
    
    /// <summary>
    /// Moving away from the opponent (opening distance).
    /// Decreases suppression buildup (more defensive).
    /// </summary>
    Retreating
}
