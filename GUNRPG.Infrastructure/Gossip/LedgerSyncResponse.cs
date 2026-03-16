using GUNRPG.Ledger;

namespace GUNRPG.Gossip;

public record LedgerSyncResponse(
    IReadOnlyList<RunLedgerEntry> Entries);
