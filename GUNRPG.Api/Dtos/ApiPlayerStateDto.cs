namespace GUNRPG.Api.Dtos;

/// <summary>
/// API-specific player/operator state DTO during combat.
/// </summary>
public sealed class ApiPlayerStateDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public float Health { get; init; }
    public float MaxHealth { get; init; }
    public float Stamina { get; init; }
    public float Fatigue { get; init; }
    public float SuppressionLevel { get; init; }
    public bool IsSuppressed { get; init; }
    public float DistanceToOpponent { get; init; }
    public int CurrentAmmo { get; init; }
    public int? MagazineSize { get; init; }
    public string AimState { get; init; } = "";
    public string MovementState { get; init; } = "";
    public string CurrentMovement { get; init; } = "";
    public string CurrentDirection { get; init; } = "";
    public string CurrentCover { get; init; } = "";
    public bool IsMoving { get; init; }
    public bool IsAlive { get; init; }
}
