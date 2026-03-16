using GUNRPG.Ledger;

namespace GUNRPG.Gossip;

public record LedgerSyncResponse
{
    public IReadOnlyList<RunLedgerEntry> Entries { get; }

    public LedgerSyncResponse(IReadOnlyList<RunLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = entries;
    }
}
