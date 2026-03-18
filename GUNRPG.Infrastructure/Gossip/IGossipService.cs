using GUNRPG.Ledger;
using GUNRPG.Security;

namespace GUNRPG.Gossip;

public interface IGossipService
{
    Task SyncAsync(CancellationToken cancellationToken = default);

    Task<bool> MergePartialValidationAsync(RunInput input, RunValidationResult validationResult, CancellationToken cancellationToken = default);

    Task BroadcastLedgerEntryAsync(RunLedgerEntry entry, CancellationToken cancellationToken = default);
}
