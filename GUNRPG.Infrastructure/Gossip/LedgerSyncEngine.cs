using GUNRPG.Ledger;

namespace GUNRPG.Gossip;

public class LedgerSyncEngine
{
    private readonly RunLedger _ledger;

    public LedgerSyncEngine(RunLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        _ledger = ledger;
    }

    public bool NeedsSync(LedgerHead peerHead)
    {
        ArgumentNullException.ThrowIfNull(peerHead);
        return peerHead.Index > _ledger.GetHead().Index;
    }

    public LedgerSyncRequest BuildSyncRequest(LedgerHead peerHead)
    {
        ArgumentNullException.ThrowIfNull(peerHead);
        var localHead = _ledger.GetHead();
        return new LedgerSyncRequest(localHead.Index + 1);
    }

    public bool ApplyResponse(LedgerSyncResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        foreach (var entry in response.Entries)
        {
            if (!_ledger.TryAppendEntry(entry))
            {
                return false;
            }
        }

        return true;
    }
}
