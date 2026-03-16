using System.Security.Cryptography;
using GUNRPG.Ledger;

namespace GUNRPG.Gossip;

public class LedgerSyncEngine
{
    private const int HashSize = SHA256.HashSizeInBytes;

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

        if (peerHead.EntryHash.Length != HashSize)
        {
            return false;
        }

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

        if (peerHead.EntryHash.Length != HashSize)
        {
            return false;
        }

        var localHead = _ledger.GetHead();
        return peerHead.Index == localHead.Index &&
               CryptographicOperations.FixedTimeEquals(peerHead.EntryHash.AsSpan(), localHead.EntryHash.AsSpan());
    }

    public LedgerSyncRequest BuildSyncRequest(LedgerHead peerHead)
    {
        ArgumentNullException.ThrowIfNull(peerHead);

        if (!NeedsSync(peerHead))
        {
            throw new InvalidOperationException(
                "Cannot build a sync request: NeedsSync(peerHead) must return true before calling BuildSyncRequest.");
        }

        var localHead = _ledger.GetHead();
        return new LedgerSyncRequest(localHead.Index + 1);
    }

    public bool ApplyResponse(LedgerSyncResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (response.Entries.Count > MaxSyncBatchSize)
        {
            return false;
        }

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
