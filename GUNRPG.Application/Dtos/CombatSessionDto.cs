using GUNRPG.Core.Combat;
using GUNRPG.Application.Sessions;

namespace GUNRPG.Application.Dtos;

public sealed class CombatSessionDto
{
    public Guid Id { get; init; }
    public SessionPhase Phase { get; init; }
    public CombatPhase CombatPhase { get; init; }
    public long CurrentTimeMs { get; init; }
    public OperatorStateDto Player { get; init; } = default!;
    public OperatorStateDto Enemy { get; init; } = default!;
    public PetStateDto Pet { get; init; } = default!;
    public long PlayerXp { get; init; }
    public int PlayerLevel { get; init; }
    public int EnemyLevel { get; init; }
    public int TurnNumber { get; init; }
    public bool IsComplete => Phase == SessionPhase.Completed || CombatPhase == CombatPhase.Ended;
}
