using System.Security.Cryptography;
using GUNRPG.Ledger;
using GUNRPG.Ledger.Indexing;

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

    public bool ResolveFork(RunLedger localLedger, IReadOnlyList<RunLedgerEntry> peerEntries)
    {
        ArgumentNullException.ThrowIfNull(localLedger);
        ArgumentNullException.ThrowIfNull(peerEntries);

        if (!RunLedger.VerifyEntries(peerEntries))
        {
            return false;
        }

        if (peerEntries.Count == 0)
        {
            return false;
        }

        var localHead = localLedger.GetHead();
        var peerHead = new LedgerHead(peerEntries[^1].Index, peerEntries[^1].EntryHash);

        if (peerHead.Index <= localHead.Index)
        {
            return false;
        }

        var peerIndex = new MerkleSkipIndex(peerEntries);
        var divergenceIndex = localLedger.MerkleSkipIndex.FindDivergenceIndex(peerIndex);

        if (divergenceIndex < 0)
        {
            divergenceIndex = localLedger.Entries.Count;
        }

        if (divergenceIndex > peerEntries.Count)
        {
            return false;
        }

        for (var i = 0; i < divergenceIndex; i++)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    localLedger.Entries[i].EntryHash.AsSpan(),
                    peerEntries[i].EntryHash.AsSpan()))
            {
                return false;
            }
        }

        localLedger.ReplaceEntriesFrom(divergenceIndex, peerEntries, (int)divergenceIndex);
        return true;
    }
}
