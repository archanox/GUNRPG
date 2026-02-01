namespace GUNRPG.Core.Intents;

/// <summary>
/// Types of intents that can be submitted.
/// </summary>
public enum IntentType
{
    // Combat
    FireWeapon,
    Reload,
    
    // Movement (legacy directional)
    WalkToward,
    WalkAway,
    SprintToward,
    SprintAway,
    SlideToward,
    SlideAway,
    
    // Movement (new state-based)
    Walk,
    Sprint,
    Crouch,
    EnterCover,
    ExitCover,
    CancelMovement,
    
    // Utility
    EnterADS,
    ExitADS,
    Stop
}

/// <summary>
/// Base class for all intents.
/// An intent is a declarative statement of future action that schedules events.
/// </summary>
public abstract class Intent
{
    public Guid OperatorId { get; set; }
    public IntentType Type { get; set; }
    public long SubmittedAtMs { get; set; }

    protected Intent(Guid operatorId, IntentType type)
    {
        OperatorId = operatorId;
        Type = type;
    }

    /// <summary>
    /// Validates if this intent can be executed given the current operator state.
    /// </summary>
    public abstract (bool isValid, string? errorMessage) Validate(Operators.Operator op);
}

/// <summary>
/// Intent to fire weapon.
/// </summary>
public class FireWeaponIntent : Intent
{
    public bool ContinuousFire { get; set; } // For full-auto

    public FireWeaponIntent(Guid operatorId) : base(operatorId, IntentType.FireWeapon)
    {
        ContinuousFire = true; // Default to full-auto
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.WeaponState == Operators.WeaponState.Reloading)
            return (false, "Cannot fire: weapon is reloading");
        
        if (op.WeaponState == Operators.WeaponState.Jammed)
            return (false, "Cannot fire: weapon is jammed");
        
        if (op.CurrentAmmo <= 0)
            return (false, "Cannot fire: no ammo");
        
        if (op.EquippedWeapon == null)
            return (false, "Cannot fire: no weapon equipped");
        
        return (true, null);
    }
}

/// <summary>
/// Intent to reload weapon.
/// </summary>
public class ReloadIntent : Intent
{
    public ReloadIntent(Guid operatorId) : base(operatorId, IntentType.Reload)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.WeaponState == Operators.WeaponState.Reloading)
            return (false, "Already reloading");
        
        if (op.EquippedWeapon == null)
            return (false, "No weapon equipped");
        
        if (op.CurrentAmmo >= op.EquippedWeapon.MagazineSize)
            return (false, "Magazine is full");
        
        return (true, null);
    }
}

/// <summary>
/// Intent to enter ADS.
/// </summary>
public class EnterADSIntent : Intent
{
    public EnterADSIntent(Guid operatorId) : base(operatorId, IntentType.EnterADS)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.AimState == Operators.AimState.ADS)
            return (false, "Already in ADS");
        
        if (op.MovementState == Operators.MovementState.Sprinting)
            return (false, "Cannot ADS while sprinting");
        
        if (op.MovementState == Operators.MovementState.Sliding)
            return (false, "Cannot ADS while sliding");
        
        return (true, null);
    }
}

/// <summary>
/// Intent to exit ADS.
/// </summary>
public class ExitADSIntent : Intent
{
    public ExitADSIntent(Guid operatorId) : base(operatorId, IntentType.ExitADS)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.AimState == Operators.AimState.Hip)
            return (false, "Already in hip-fire");
        
        return (true, null);
    }
}

/// <summary>
/// Base class for movement intents.
/// </summary>
public abstract class MovementIntent : Intent
{
    public bool TowardOpponent { get; set; }

    protected MovementIntent(Guid operatorId, IntentType type, bool towardOpponent) 
        : base(operatorId, type)
    {
        TowardOpponent = towardOpponent;
    }
}

/// <summary>
/// Intent to walk.
/// </summary>
public class WalkIntent : MovementIntent
{
    public WalkIntent(Guid operatorId, bool towardOpponent) 
        : base(operatorId, towardOpponent ? IntentType.WalkToward : IntentType.WalkAway, towardOpponent)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        return (true, null); // Walking is always valid
    }
}

/// <summary>
/// Intent to sprint.
/// </summary>
public class SprintIntent : MovementIntent
{
    public SprintIntent(Guid operatorId, bool towardOpponent) 
        : base(operatorId, towardOpponent ? IntentType.SprintToward : IntentType.SprintAway, towardOpponent)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.Stamina <= 0)
            return (false, "Cannot sprint: no stamina");
        
        // Sprinting auto-exits ADS
        
        return (true, null);
    }
}

/// <summary>
/// Intent to slide.
/// </summary>
public class SlideIntent : MovementIntent
{
    public SlideIntent(Guid operatorId, bool towardOpponent) 
        : base(operatorId, towardOpponent ? IntentType.SlideToward : IntentType.SlideAway, towardOpponent)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.Stamina < op.SlideStaminaCost)
            return (false, $"Cannot slide: need {op.SlideStaminaCost} stamina");
        
        if (op.MovementState == Operators.MovementState.Sliding)
            return (false, "Already sliding");
        
        return (true, null);
    }
}

/// <summary>
/// Intent to stop all actions.
/// </summary>
public class StopIntent : Intent
{
    public StopIntent(Guid operatorId) : base(operatorId, IntentType.Stop)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        return (true, null); // Always valid
    }
}

/// <summary>
/// Intent to start walking (state-based movement).
/// </summary>
public class WalkStateIntent : Intent
{
    public long DurationMs { get; set; }

    public WalkStateIntent(Guid operatorId, long durationMs = 1000) : base(operatorId, IntentType.Walk)
    {
        DurationMs = durationMs;
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        return (true, null); // Walking is always valid
    }
}

/// <summary>
/// Intent to start sprinting (state-based movement).
/// </summary>
public class SprintStateIntent : Intent
{
    public long DurationMs { get; set; }

    public SprintStateIntent(Guid operatorId, long durationMs = 2000) : base(operatorId, IntentType.Sprint)
    {
        DurationMs = durationMs;
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.Stamina <= 0)
            return (false, "Cannot sprint: no stamina");
        
        return (true, null);
    }
}

/// <summary>
/// Intent to crouch (state-based movement).
/// </summary>
public class CrouchIntent : Intent
{
    public long DurationMs { get; set; }

    public CrouchIntent(Guid operatorId, long durationMs = 5000) : base(operatorId, IntentType.Crouch)
    {
        DurationMs = durationMs;
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        return (true, null); // Crouching is always valid
    }
}

/// <summary>
/// Intent to enter cover.
/// </summary>
public class EnterCoverIntent : Intent
{
    public Operators.CoverState CoverType { get; set; }

    public EnterCoverIntent(Guid operatorId, Operators.CoverState coverType) : base(operatorId, IntentType.EnterCover)
    {
        CoverType = coverType;
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (!Combat.MovementModel.CanEnterCover(op.CurrentMovement))
            return (false, "Cannot enter cover while moving (must be stationary or crouching)");
        
        if (op.CurrentCover != Operators.CoverState.None)
            return (false, "Already in cover");
        
        return (true, null);
    }
}

/// <summary>
/// Intent to exit cover.
/// </summary>
public class ExitCoverIntent : Intent
{
    public ExitCoverIntent(Guid operatorId) : base(operatorId, IntentType.ExitCover)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (op.CurrentCover == Operators.CoverState.None)
            return (false, "Not in cover");
        
        return (true, null);
    }
}

/// <summary>
/// Intent to cancel current movement.
/// </summary>
public class CancelMovementIntent : Intent
{
    public CancelMovementIntent(Guid operatorId) : base(operatorId, IntentType.CancelMovement)
    {
    }

    public override (bool isValid, string? errorMessage) Validate(Operators.Operator op)
    {
        if (!op.IsMoving)
            return (false, "Not currently moving");
        
        return (true, null);
    }
}
