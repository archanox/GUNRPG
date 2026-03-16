using System.Security.Cryptography;
using GUNRPG.Ledger;

namespace GUNRPG.Gossip;

public class LedgerSyncEngine
{
    public const int MaxSyncBatchSize = 256;

    private readonly RunLedger _ledger;

    public LedgerSyncEngine(RunLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        _ledger = ledger;
    }

    public bool NeedsSync(LedgerHead peerHead)
    {
        ArgumentNullException.ThrowIfNull(peerHead);
        var localHead = _ledger.GetHead();

        if (peerHead.Index == localHead.Index &&
            !CryptographicOperations.FixedTimeEquals(peerHead.EntryHash.AsSpan(), localHead.EntryHash.AsSpan()))
        {
            // Fork detected — same index but diverging hashes; reject sync
            return false;
        }

        return peerHead.Index > localHead.Index;
    }

    public bool IsSameHead(LedgerHead peerHead)
    {
        ArgumentNullException.ThrowIfNull(peerHead);
        var localHead = _ledger.GetHead();
        return peerHead.Index == localHead.Index &&
               CryptographicOperations.FixedTimeEquals(peerHead.EntryHash.AsSpan(), localHead.EntryHash.AsSpan());
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
