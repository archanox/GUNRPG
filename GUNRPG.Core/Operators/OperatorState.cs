namespace GUNRPG.Core.Operators;

/// <summary>
/// Movement state of an operator.
/// </summary>
public enum MovementState
{
    Idle,
    Walking,
    Sprinting,
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
