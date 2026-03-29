using LiteDB;

namespace GUNRPG.Infrastructure.Persistence;

public sealed class OperatorStatsDocument
{
    [BsonId]
    public Guid OperatorId { get; set; }
    public int InfilCount { get; set; }
    public int ExfilCount { get; set; }
    public long TotalInfilDurationTicks { get; set; }
    public int EnemyKills { get; set; }
}
