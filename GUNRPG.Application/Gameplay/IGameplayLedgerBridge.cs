using GUNRPG.Core.Operators;
using GUNRPG.Security;

namespace GUNRPG.Application.Gameplay;

public interface IGameplayLedgerBridge
{
    /// <summary>
    /// Mirrors a run into the ledger.  The <paramref name="runInput"/> describes the player's
    /// intent (actions + seed); <paramref name="operatorEvents"/> are the server-side events that
    /// were produced by the operator pipeline for projection purposes.
    /// The OperatorEvents are NEVER part of RunInput — they are supplied separately by the
    /// server and are used only to enrich the ledger mutation for state-projection queries.
    /// </summary>
    Task MirrorAsync(
        RunInput runInput,
        IReadOnlyList<OperatorEvent> operatorEvents,
        CancellationToken cancellationToken = default);

    Task<OperatorAggregate?> LoadProjectedOperatorAsync(
        OperatorId operatorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperatorId>> ListProjectedOperatorsAsync(CancellationToken cancellationToken = default);

    Task<GameState> ProjectAsync(CancellationToken cancellationToken = default);
}
