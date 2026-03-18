using GUNRPG.Core.Intents;

namespace GUNRPG.Security;

public sealed record PlayerAction
{
    public long SequenceNumber { get; init; }

    public PrimaryAction? Primary { get; init; }

    public MovementAction? Movement { get; init; }

    public StanceAction? Stance { get; init; }

    public CoverAction? Cover { get; init; }

    public bool CancelMovement { get; init; }
}
