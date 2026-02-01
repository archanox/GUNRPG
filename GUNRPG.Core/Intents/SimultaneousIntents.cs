using GUNRPG.Core.Operators;

namespace GUNRPG.Core.Intents;

/// <summary>
/// Primary combat actions (mutually exclusive).
/// </summary>
public enum PrimaryAction
{
    None,
    Fire,
    Reload
}

/// <summary>
/// Movement actions (mutually exclusive).
/// </summary>
public enum MovementAction
{
    Stand,
    WalkToward,
    WalkAway,
    SprintToward,
    SprintAway,
    SlideToward,
    SlideAway,
    
    // New state-based movement
    Walk,
    Sprint,
    Crouch
}

/// <summary>
/// Cover actions.
/// </summary>
public enum CoverAction
{
    None,
    EnterPartial,
    EnterFull,
    Exit
}

/// <summary>
/// Stance actions (state changes).
/// </summary>
public enum StanceAction
{
    None,
    EnterADS,
    ExitADS
}

/// <summary>
/// Container for simultaneous intents that can be active at the same time.
/// </summary>
public class SimultaneousIntents
{
    public Guid OperatorId { get; set; }
    public PrimaryAction Primary { get; set; }
    public MovementAction Movement { get; set; }
    public StanceAction Stance { get; set; }
    public CoverAction Cover { get; set; }
    public bool CancelMovement { get; set; }
    public long SubmittedAtMs { get; set; }

    public SimultaneousIntents(Guid operatorId)
    {
        OperatorId = operatorId;
        Primary = PrimaryAction.None;
        Movement = MovementAction.Stand;
        Stance = StanceAction.None;
        Cover = CoverAction.None;
        CancelMovement = false;
    }

    /// <summary>
    /// Validates if these intents can be executed given the current operator state.
    /// </summary>
    public (bool isValid, string? errorMessage) Validate(Operator op)
    {
        // Validate primary action
        var primaryResult = ValidatePrimary(op);
        if (!primaryResult.isValid)
            return primaryResult;

        // Validate movement action
        var movementResult = ValidateMovement(op);
        if (!movementResult.isValid)
            return movementResult;

        // Validate stance action
        var stanceResult = ValidateStance(op);
        if (!stanceResult.isValid)
            return stanceResult;

        // Validate combinations
        var combinationResult = ValidateCombinations(op);
        if (!combinationResult.isValid)
            return combinationResult;

        return (true, null);
    }

    private (bool isValid, string? errorMessage) ValidatePrimary(Operator op)
    {
        switch (Primary)
        {
            case PrimaryAction.Fire:
                if (op.WeaponState == WeaponState.Reloading)
                    return (false, "Cannot fire: weapon is reloading");
                if (op.WeaponState == WeaponState.Jammed)
                    return (false, "Cannot fire: weapon is jammed");
                if (op.CurrentAmmo <= 0)
                    return (false, "Cannot fire: no ammo");
                if (op.EquippedWeapon == null)
                    return (false, "Cannot fire: no weapon equipped");
                break;

            case PrimaryAction.Reload:
                if (op.WeaponState == WeaponState.Reloading)
                    return (false, "Already reloading");
                if (op.EquippedWeapon == null)
                    return (false, "No weapon equipped");
                if (op.CurrentAmmo >= op.EquippedWeapon.MagazineSize)
                    return (false, "Magazine is full");
                break;
        }

        return (true, null);
    }

    private (bool isValid, string? errorMessage) ValidateMovement(Operator op)
    {
        switch (Movement)
        {
            case MovementAction.SprintToward:
            case MovementAction.SprintAway:
            case MovementAction.Sprint:
                if (op.Stamina <= 0)
                    return (false, "Cannot sprint: no stamina");
                break;

            case MovementAction.SlideToward:
            case MovementAction.SlideAway:
                if (op.Stamina < op.SlideStaminaCost)
                    return (false, $"Cannot slide: need {op.SlideStaminaCost} stamina");
                if (op.MovementState == MovementState.Sliding)
                    return (false, "Already sliding");
                break;
        }

        return (true, null);
    }

    private (bool isValid, string? errorMessage) ValidateStance(Operator op)
    {
        switch (Stance)
        {
            case StanceAction.EnterADS:
                if (op.AimState == AimState.ADS)
                    return (false, "Already in ADS");
                // Note: Can now initiate ADS while sprinting - it will just take longer
                if (op.MovementState == MovementState.Sliding)
                    return (false, "Cannot ADS while sliding");
                break;

            case StanceAction.ExitADS:
                if (op.AimState == AimState.Hip)
                    return (false, "Already in hip-fire");
                break;
        }

        return (true, null);
    }

    private (bool isValid, string? errorMessage) ValidateCombinations(Operator op)
    {
        // Validate cover actions
        if (Cover != CoverAction.None)
        {
            if (Cover == CoverAction.Exit && op.CurrentCover == CoverState.None)
                return (false, "Not in cover");
            
            if ((Cover == CoverAction.EnterPartial || Cover == CoverAction.EnterFull) && op.CurrentCover != CoverState.None)
                return (false, "Already in cover");
            
            // Can only enter cover when stationary or crouching
            if ((Cover == CoverAction.EnterPartial || Cover == CoverAction.EnterFull) && 
                !Combat.MovementModel.CanEnterCover(op.CurrentMovement))
                return (false, "Can only enter cover when stationary or crouching");
        }

        // Cannot cancel movement if not moving
        if (CancelMovement && !op.IsMoving)
            return (false, "Not currently moving");

        // Cannot initiate ADS while actively firing
        if (Stance == StanceAction.EnterADS && Primary == PrimaryAction.Fire)
        {
            // Check if already actively firing (not just starting)
            // This will be handled in the combat system - allow it for now
        }

        // Sprinting auto-exits ADS (handled in processing)
        if ((Movement == MovementAction.SprintToward || Movement == MovementAction.SprintAway || Movement == MovementAction.Sprint))
        {
            // This is valid, but will auto-exit ADS in processing
        }

        // Cannot reload while sliding
        if (Primary == PrimaryAction.Reload && (Movement == MovementAction.SlideToward || Movement == MovementAction.SlideAway))
        {
            return (false, "Cannot reload while sliding");
        }

        return (true, null);
    }

    /// <summary>
    /// Checks if any action is specified.
    /// </summary>
    public bool HasAnyAction()
    {
        return Primary != PrimaryAction.None ||
               Movement != MovementAction.Stand ||
               Stance != StanceAction.None ||
               Cover != CoverAction.None ||
               CancelMovement;
    }

    /// <summary>
    /// Creates a stop intent (all actions set to None).
    /// </summary>
    public static SimultaneousIntents CreateStop(Guid operatorId)
    {
        return new SimultaneousIntents(operatorId);
    }
}
