using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Dtos;

public sealed class OperatorStateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public float Health { get; init; }
    public float MaxHealth { get; init; }
    public float Stamina { get; init; }
    public float Fatigue { get; init; }
    public float SuppressionLevel { get; init; }
    public bool IsSuppressed { get; init; }
    public float DistanceToOpponent { get; init; }
    public int CurrentAmmo { get; init; }
    public int? MagazineSize { get; init; }
    public AimState AimState { get; init; }
    public MovementState MovementState { get; init; }
    public MovementState CurrentMovement { get; init; }
    public MovementDirection CurrentDirection { get; init; }
    public CoverState CurrentCover { get; init; }
    public bool IsMoving { get; init; }
    public bool IsAlive { get; init; }
}
