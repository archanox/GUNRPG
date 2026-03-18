using GUNRPG.Ledger;
using GUNRPG.Security;

namespace GUNRPG.Gossip;

public interface IGossipPeerClient
{
    Task<LedgerHead> GetLedgerHeadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunLedgerEntry>> GetEntriesFromAsync(long fromIndex, int maxCount, CancellationToken cancellationToken = default);

    Task BroadcastPartialValidationAsync(RunInput input, RunValidationResult validationResult, CancellationToken cancellationToken = default);

    Task BroadcastLedgerEntryAsync(RunLedgerEntry entry, CancellationToken cancellationToken = default);
}
