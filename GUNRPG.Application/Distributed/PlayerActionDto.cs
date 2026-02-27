using GUNRPG.Core.Intents;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Represents a player action submitted to the distributed authority.
/// Wraps intent data with metadata for replication.
/// </summary>
public sealed class PlayerActionDto
{
    public Guid ActionId { get; init; } = Guid.NewGuid();
    public Guid OperatorId { get; init; }
    public PrimaryAction? Primary { get; init; }
    public MovementAction? Movement { get; init; }
    public StanceAction? Stance { get; init; }
    public CoverAction? Cover { get; init; }
    public bool CancelMovement { get; init; }
}
