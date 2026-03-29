using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Operators;

public sealed record OperatorStats(
    Guid OperatorId,
    int InfilCount,
    int ExfilCount,
    long TotalInfilDurationTicks,
    int EnemyKills);

public sealed record RunStats(
    Guid OperatorId,
    bool SuccessfulExfil,
    long InfilDurationTicks,
    int EnemyKills);

public static class RunStatsExtractor
{
    public static RunStats? ExtractLatest(IReadOnlyList<OperatorEvent> events)
    {
        return ExtractCompletedRuns(events).LastOrDefault();
    }

    public static IReadOnlyList<RunStats> ExtractCompletedRuns(IReadOnlyList<OperatorEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var completedRuns = new List<RunStats>();
        DateTimeOffset? infilStartedAt = null;
        int enemyKills = 0;

        foreach (var evt in events.OrderBy(e => e.SequenceNumber))
        {
            switch (evt)
            {
                case InfilStartedEvent started:
                    infilStartedAt = started.GetPayload().InfilStartTime;
                    enemyKills = 0;
                    break;

                case CombatVictoryEvent when infilStartedAt.HasValue:
                    enemyKills++;
                    break;

                case InfilEndedEvent ended when infilStartedAt.HasValue:
                    var (wasSuccessful, _) = ended.GetPayload();
                    var infilDurationTicks = Math.Max(0L, (ended.Timestamp - infilStartedAt.Value).Ticks);
                    completedRuns.Add(new RunStats(
                        ended.OperatorId.Value,
                        wasSuccessful,
                        infilDurationTicks,
                        enemyKills));
                    infilStartedAt = null;
                    enemyKills = 0;
                    break;
            }
        }

        return completedRuns;
    }
}
