using GUNRPG.Application.Operators;
using LiteDB;

namespace GUNRPG.Infrastructure.Persistence;

public sealed class LiteDbOperatorStatsStore : IOperatorStatsStore
{
    private readonly LiteDatabase _database;
    private readonly ILiteCollection<OperatorStatsDocument> _stats;

    public LiteDbOperatorStatsStore(LiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _stats = _database.GetCollection<OperatorStatsDocument>("operator_stats");
        _stats.EnsureIndex(x => x.OperatorId, true);
    }

    public Task<OperatorStats?> TryGetAsync(Guid operatorId)
    {
        var document = _stats.FindById(operatorId);
        return Task.FromResult(document is null ? null : ToModel(document));
    }

    public Task<OperatorStats> ApplyRunStatsAsync(RunStats runStats)
    {
        _database.BeginTrans();
        try
        {
            var document = _stats.FindById(runStats.OperatorId) ?? new OperatorStatsDocument
            {
                OperatorId = runStats.OperatorId
            };

            document.InfilCount = checked(document.InfilCount + 1);
            document.ExfilCount = checked(document.ExfilCount + (runStats.SuccessfulExfil ? 1 : 0));
            document.TotalInfilDurationTicks = checked(document.TotalInfilDurationTicks + runStats.InfilDurationTicks);
            document.EnemyKills = checked(document.EnemyKills + runStats.EnemyKills);

            _stats.Upsert(document);
            _database.Commit();
            return Task.FromResult(ToModel(document));
        }
        catch
        {
            _database.Rollback();
            throw;
        }
    }

    public Task UpsertAsync(OperatorStats stats)
    {
        _stats.Upsert(new OperatorStatsDocument
        {
            OperatorId = stats.OperatorId,
            InfilCount = stats.InfilCount,
            ExfilCount = stats.ExfilCount,
            TotalInfilDurationTicks = stats.TotalInfilDurationTicks,
            EnemyKills = stats.EnemyKills
        });
        return Task.CompletedTask;
    }

    private static OperatorStats ToModel(OperatorStatsDocument document)
    {
        return new OperatorStats(
            document.OperatorId,
            document.InfilCount,
            document.ExfilCount,
            document.TotalInfilDurationTicks,
            document.EnemyKills);
    }
}
