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
