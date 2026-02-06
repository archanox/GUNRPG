namespace GUNRPG.Api.Dtos;

/// <summary>
/// API-specific combat session DTO.
/// </summary>
public sealed class ApiCombatSessionDto
{
    public Guid Id { get; init; }
    public string Phase { get; init; } = "";
    public long CurrentTimeMs { get; init; }
    public ApiOperatorStateDto Player { get; init; } = default!;
    public ApiOperatorStateDto Enemy { get; init; } = default!;
    public ApiPetStateDto Pet { get; init; } = default!;
    public long PlayerXp { get; init; }
    public int PlayerLevel { get; init; }
    public int EnemyLevel { get; init; }
    public int TurnNumber { get; init; }
}
