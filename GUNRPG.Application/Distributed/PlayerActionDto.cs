using GUNRPG.Core.Intents;
using GUNRPG.Application.Sessions;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Represents a player action submitted to the distributed authority.
/// Wraps intent data with metadata for replication.
/// </summary>
public sealed class PlayerActionDto
{
    private IReadOnlyList<IntentSnapshot>? _replayTurns;

    public Guid ActionId { get; init; } = Guid.NewGuid();
    public Guid OperatorId { get; init; }
    public Guid? SessionId { get; init; }
    public PrimaryAction? Primary { get; init; }
    public MovementAction? Movement { get; init; }
    public StanceAction? Stance { get; init; }
    public CoverAction? Cover { get; init; }
    public bool CancelMovement { get; init; }
    public string? ReplayInitialSnapshotJson { get; init; }
    public IReadOnlyList<IntentSnapshot>? ReplayTurns
    {
        get => _replayTurns;
        init
        {
            _replayTurns = value == null
                ? null
                : value.Select(CloneIntent).ToArray();
        }
    }

    public PlayerActionDto Clone()
    {
        return new PlayerActionDto
        {
            ActionId = ActionId,
            OperatorId = OperatorId,
            SessionId = SessionId,
            Primary = Primary,
            Movement = Movement,
            Stance = Stance,
            Cover = Cover,
            CancelMovement = CancelMovement,
            ReplayInitialSnapshotJson = ReplayInitialSnapshotJson,
            ReplayTurns = ReplayTurns?.Select(CloneIntent).ToArray()
        };
    }

    private static IntentSnapshot CloneIntent(IntentSnapshot snapshot)
    {
        return new IntentSnapshot
        {
            OperatorId = snapshot.OperatorId,
            Primary = snapshot.Primary,
            Movement = snapshot.Movement,
            Stance = snapshot.Stance,
            Cover = snapshot.Cover,
            CancelMovement = snapshot.CancelMovement,
            SubmittedAtMs = snapshot.SubmittedAtMs
        };
    }
}
