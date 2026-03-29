namespace GUNRPG.Api.Dtos;

public sealed class ApiOperatorStatsDto
{
    public Guid OperatorId { get; init; }
    public int InfilCount { get; init; }
    public int ExfilCount { get; init; }
    public long TotalInfilDurationTicks { get; init; }
    public int EnemyKills { get; init; }
}
