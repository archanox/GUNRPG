namespace GUNRPG.Application.Requests;

public sealed class SessionCreateRequest
{
    public Guid? Id { get; init; }
    public Guid? OperatorId { get; init; }
    public string? PlayerName { get; init; }
    public string? EnemyName { get; init; }
    public int? Seed { get; init; }
    public float? StartingDistance { get; init; }
}
