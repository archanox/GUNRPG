using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Gameplay;

public interface IGameplayLedgerBridge
{
    Task MirrorAsync(
        Guid runId,
        OperatorId operatorId,
        IReadOnlyList<OperatorEvent> operatorEvents,
        IReadOnlyList<GUNRPG.Ledger.GameplayLedgerEvent>? gameplayEvents = null,
        CancellationToken cancellationToken = default);

    Task<OperatorAggregate?> LoadProjectedOperatorAsync(
        OperatorId operatorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperatorId>> ListProjectedOperatorsAsync(CancellationToken cancellationToken = default);

    Task<GameState> ProjectAsync(CancellationToken cancellationToken = default);
}
