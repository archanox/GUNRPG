using GUNRPG.Application.Sessions;

namespace GUNRPG.Application.Dtos;

public sealed class CombatSessionDto
{
    public Guid Id { get; init; }
    public Guid OperatorId { get; init; }
    public SessionPhase Phase { get; init; }
    public long CurrentTimeMs { get; init; }
    public PlayerStateDto Player { get; init; } = default!;
    public PlayerStateDto Enemy { get; init; } = default!;
    public PetStateDto Pet { get; init; } = default!;
    public int EnemyLevel { get; init; }
    public int TurnNumber { get; init; }
}
