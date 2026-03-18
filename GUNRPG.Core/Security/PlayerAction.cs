namespace GUNRPG.Security;

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
/// Base type for all player actions representing intent submitted via RunInput.
/// Actions are processed sequentially by the replay engine using a seeded RNG.
/// </summary>
public abstract record PlayerAction;

/// <summary>
/// Intent to move the player in the given direction.
/// </summary>
public record MoveAction(Direction Direction) : PlayerAction;

/// <summary>
/// Intent to attack the target with the given identifier.
/// </summary>
public record AttackAction(Guid TargetId) : PlayerAction;

/// <summary>
/// Intent to use (consume or activate) the item with the given identifier.
/// </summary>
public record UseItemAction(Guid ItemId) : PlayerAction;

/// <summary>
/// Intent to exfiltrate (end the run successfully).
/// </summary>
public record ExfilAction() : PlayerAction;
