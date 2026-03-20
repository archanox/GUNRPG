namespace GUNRPG.Core.Simulation;

/// <summary>
/// Cardinal directions for movement actions.
/// </summary>
public enum Direction
{
    North,
    South,
    East,
    West
}

/// <summary>
/// Base type for all player actions representing intent submitted via replayable simulation input.
/// </summary>
public abstract record PlayerAction;

/// <summary>
/// Intent to move the player in the given direction.
/// </summary>
public sealed record MoveAction : PlayerAction
{
    public MoveAction(Direction direction)
    {
        Direction = direction;
    }

    public Direction Direction { get; }
}

/// <summary>
/// Intent to attack the target with the given identifier.
/// </summary>
public sealed record AttackAction(Guid TargetId) : PlayerAction;

/// <summary>
/// Intent to use (consume or activate) the item with the given identifier.
/// </summary>
public sealed record UseItemAction(Guid ItemId) : PlayerAction;

/// <summary>
/// Intent to exfiltrate (end the run successfully).
/// </summary>
public sealed record ExfilAction() : PlayerAction;
