namespace GUNRPG.Application.Dtos;

public sealed class OperatorStatsDto
{
    public Guid OperatorId { get; init; }
    public int InfilCount { get; init; }
    public int ExfilCount { get; init; }
    public long TotalInfilDurationTicks { get; init; }
    public int EnemyKills { get; init; }
}
