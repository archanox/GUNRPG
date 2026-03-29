using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Operators;

public sealed class OperatorStatsService
{
    private readonly IOperatorStatsStore _statsStore;
    private readonly IOperatorEventStore _eventStore;

    public OperatorStatsService(IOperatorStatsStore statsStore, IOperatorEventStore eventStore)
    {
        _statsStore = statsStore ?? throw new ArgumentNullException(nameof(statsStore));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
    }

    public async Task UpdateStatsAsync(RunStats runStats)
    {
        await _statsStore.ApplyRunStatsAsync(runStats);
    }

    public async Task<OperatorStats> GetStatsAsync(Guid operatorId)
    {
        var existing = await _statsStore.TryGetAsync(operatorId);
        if (existing is not null)
        {
            return existing;
        }

        var events = await _eventStore.LoadEventsAsync(OperatorId.FromGuid(operatorId));
        var rebuilt = RebuildStats(operatorId, events);
        await _statsStore.UpsertAsync(rebuilt);
        return rebuilt;
    }

    private static OperatorStats RebuildStats(Guid operatorId, IReadOnlyList<OperatorEvent> events)
    {
        var stats = new OperatorStats(operatorId, 0, 0, 0, 0);
        foreach (var runStats in RunStatsExtractor.ExtractCompletedRuns(events))
        {
            stats = stats with
            {
                InfilCount = checked(stats.InfilCount + 1),
                ExfilCount = checked(stats.ExfilCount + (runStats.SuccessfulExfil ? 1 : 0)),
                TotalInfilDurationTicks = checked(stats.TotalInfilDurationTicks + runStats.InfilDurationTicks),
                EnemyKills = checked(stats.EnemyKills + runStats.EnemyKills)
            };
        }

        return stats;
    }
}
