namespace GUNRPG.Application.Operators;

public interface IOperatorStatsStore
{
    Task<OperatorStats?> TryGetAsync(Guid operatorId);
    Task<OperatorStats> ApplyRunStatsAsync(RunStats runStats);
    Task UpsertAsync(OperatorStats stats);
}
