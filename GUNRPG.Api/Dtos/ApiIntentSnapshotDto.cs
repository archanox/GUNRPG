using GUNRPG.Core.Intents;

namespace GUNRPG.Api.Dtos;

public sealed class ApiIntentSnapshotDto
{
    public Guid OperatorId { get; init; }
    public PrimaryAction Primary { get; init; }
    public MovementAction Movement { get; init; }
    public StanceAction Stance { get; init; }
    public CoverAction Cover { get; init; }
    public bool CancelMovement { get; init; }
    public long SubmittedAtMs { get; init; }
}
