namespace GUNRPG.Application.Requests;

public sealed class SessionCreateRequest
{
    public string? PlayerName { get; init; }
    public string? EnemyName { get; init; }
    public int? Seed { get; init; }
    public float? StartingDistance { get; init; }
}
