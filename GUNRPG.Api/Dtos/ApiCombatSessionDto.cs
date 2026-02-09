namespace GUNRPG.Api.Dtos;

/// <summary>
/// API-specific combat session DTO.
/// </summary>
public sealed class ApiCombatSessionDto
{
    public Guid Id { get; init; }
    public Guid OperatorId { get; init; }
    public string Phase { get; init; } = "";
    public long CurrentTimeMs { get; init; }
    public ApiPlayerStateDto Player { get; init; } = default!;
    public ApiPlayerStateDto Enemy { get; init; } = default!;
    public ApiPetStateDto Pet { get; init; } = default!;
    public int EnemyLevel { get; init; }
    public int TurnNumber { get; init; }
}
