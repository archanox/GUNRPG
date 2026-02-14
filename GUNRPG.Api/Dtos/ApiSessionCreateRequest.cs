namespace GUNRPG.Api.Dtos;

/// <summary>
/// API request for creating a session.
/// </summary>
public sealed class ApiSessionCreateRequest
{
    public Guid? Id { get; init; }
    public Guid? OperatorId { get; init; }
    public string? PlayerName { get; init; }
    public int? Seed { get; init; }
    public float? StartingDistance { get; init; }
    public string? EnemyName { get; init; }
}
