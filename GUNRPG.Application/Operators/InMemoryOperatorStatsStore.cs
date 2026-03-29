using System.Collections.Concurrent;

namespace GUNRPG.Application.Operators;

public sealed class InMemoryOperatorStatsStore : IOperatorStatsStore
{
    private readonly ConcurrentDictionary<Guid, OperatorStats> _stats = new();

    public Task<OperatorStats?> TryGetAsync(Guid operatorId)
    {
        return Task.FromResult(_stats.TryGetValue(operatorId, out var stats) ? stats : null);
    }

    public Task<OperatorStats> ApplyRunStatsAsync(RunStats runStats)
    {
        var updated = _stats.AddOrUpdate(
            runStats.OperatorId,
            _ => new OperatorStats(
                runStats.OperatorId,
                1,
                runStats.SuccessfulExfil ? 1 : 0,
                runStats.InfilDurationTicks,
                runStats.EnemyKills),
            (_, current) => current with
            {
                InfilCount = checked(current.InfilCount + 1),
                ExfilCount = checked(current.ExfilCount + (runStats.SuccessfulExfil ? 1 : 0)),
                TotalInfilDurationTicks = checked(current.TotalInfilDurationTicks + runStats.InfilDurationTicks),
                EnemyKills = checked(current.EnemyKills + runStats.EnemyKills)
            });

        return Task.FromResult(updated);
    }

    public Task UpsertAsync(OperatorStats stats)
    {
        _stats[stats.OperatorId] = stats;
        return Task.CompletedTask;
    }
}
