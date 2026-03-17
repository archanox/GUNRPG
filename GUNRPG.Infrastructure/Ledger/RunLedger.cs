using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using GUNRPG.Gossip;
using GUNRPG.Ledger.Indexing;
using GUNRPG.Security;

namespace GUNRPG.Ledger;

public class RunLedger
{
    private const int HashSize = SHA256.HashSizeInBytes;
    private const int GuidSize = 16;
    private const int Int64Size = 8;

    private static readonly ImmutableArray<byte> ZeroHash = ImmutableArray.Create(new byte[HashSize]);

    private readonly List<RunLedgerEntry> _entries = [];
    private readonly ReadOnlyCollection<RunLedgerEntry> _readOnlyEntries;
    private readonly MerkleSkipIndex _merkleSkipIndex;

    public RunLedger()
    {
        _readOnlyEntries = _entries.AsReadOnly();
        _merkleSkipIndex = new MerkleSkipIndex(GetEntryHashAt);
    }

    public IReadOnlyList<RunLedgerEntry> Entries => _readOnlyEntries;

    public RunLedgerEntry? Head => _entries.Count == 0 ? null : _entries[^1];

    public MerkleSkipIndex MerkleSkipIndex => _merkleSkipIndex;

    public RunLedgerEntry Append(RunValidationResult run)
    {
        return Append(run, DateTimeOffset.UtcNow);
    }

    public bool TryAppendWithQuorum(
        RunValidationResult run,
        QuorumValidator quorumValidator,
        AuthoritySet authoritySet,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendWithQuorum(run, quorumValidator, authoritySet, quorumPolicy, DateTimeOffset.UtcNow);
    }

    internal RunLedgerEntry Append(RunValidationResult run, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(run);

        var index = (long)_entries.Count;
        var previousHash = _entries.Count == 0 ? ZeroHash : _entries[^1].EntryHash;

        var entryHash = ComputeEntryHash(index, previousHash, timestamp, run);

        var entry = new RunLedgerEntry(index, previousHash, entryHash, timestamp, run);
        _entries.Add(entry);
        _merkleSkipIndex.Append(entry);
        return entry;
    }

    internal bool TryAppendWithQuorum(
        RunValidationResult run,
        QuorumValidator quorumValidator,
        AuthoritySet authoritySet,
        QuorumPolicy quorumPolicy,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(quorumValidator);
        ArgumentNullException.ThrowIfNull(authoritySet);
        ArgumentNullException.ThrowIfNull(quorumPolicy);

        if (!quorumValidator.HasQuorum(run.Attestation, authoritySet, quorumPolicy))
        {
            return false;
        }

        Append(run, timestamp);
        return true;
    }

    public bool Verify()
    {
        return VerifyEntries(_entries);
    }

    public LedgerHead GetHead()
    {
        if (_entries.Count == 0)
        {
            return new LedgerHead(-1, ZeroHash);
        }

        var head = _entries[^1];
        return new LedgerHead(head.Index, head.EntryHash);
    }

    public IReadOnlyList<RunLedgerEntry> GetEntriesFrom(long fromIndex, int maxCount = int.MaxValue)
    {
        if (maxCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be non-negative.");
        }

        if (fromIndex < 0 || fromIndex >= _entries.Count)
        {
            return [];
        }

        var count = Math.Min(_entries.Count - (int)fromIndex, maxCount);
        return _entries.GetRange((int)fromIndex, count).AsReadOnly();
    }

    public bool TryAppendEntry(RunLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Run is null)
        {
            return false;
        }

        var expectedIndex = (long)_entries.Count;
        if (entry.Index != expectedIndex)
        {
            return false;
        }

        if (entry.EntryHash.Length != HashSize || entry.PreviousHash.Length != HashSize)
        {
            return false;
        }

        var expectedPreviousHash = _entries.Count == 0 ? ZeroHash : _entries[^1].EntryHash;
        if (!CryptographicOperations.FixedTimeEquals(entry.PreviousHash.AsSpan(), expectedPreviousHash.AsSpan()))
        {
            return false;
        }

        var recomputed = ComputeEntryHash(entry.Index, entry.PreviousHash, entry.Timestamp, entry.Run);
        if (!CryptographicOperations.FixedTimeEquals(entry.EntryHash.AsSpan(), recomputed.AsSpan()))
        {
            return false;
        }

        _entries.Add(entry);
        _merkleSkipIndex.Append(entry);
        return true;
    }

    // Replaces an entry at the given index — internal for tamper-detection testing only.
    internal void ReplaceEntryForTest(int index, RunLedgerEntry entry)
    {
        if (entry.Index != index)
        {
            throw new ArgumentException("Replacement entry index must match the target index.", nameof(entry));
        }

        _entries[index] = entry;
        _merkleSkipIndex.Update(entry);
    }

    internal void ReplaceEntriesFrom(long divergenceIndex, IReadOnlyList<RunLedgerEntry> entries, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (divergenceIndex < 0 || divergenceIndex > _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(divergenceIndex));
        }

        if (startIndex < 0 || startIndex > entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        _entries.RemoveRange((int)divergenceIndex, _entries.Count - (int)divergenceIndex);
        for (var i = startIndex; i < entries.Count; i++)
        {
            _entries.Add(entries[i]);
        }

        _merkleSkipIndex.Rebuild(_entries);
    }

    internal static bool VerifyEntries(IReadOnlyList<RunLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.Index != i)
            {
                return false;
            }

            if (entry.Run is null || entry.EntryHash.Length != HashSize || entry.PreviousHash.Length != HashSize)
            {
                return false;
            }

            var recomputed = ComputeEntryHash(entry.Index, entry.PreviousHash, entry.Timestamp, entry.Run);
            if (!CryptographicOperations.FixedTimeEquals(entry.EntryHash.AsSpan(), recomputed.AsSpan()))
            {
                return false;
            }

            var expectedPreviousHash = i == 0 ? ZeroHash : entries[i - 1].EntryHash;
            if (expectedPreviousHash.Length != HashSize)
            {
                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(entry.PreviousHash.AsSpan(), expectedPreviousHash.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }

    internal static ImmutableArray<byte> ComputeEntryHash(
        long index,
        ImmutableArray<byte> previousHash,
        DateTimeOffset timestamp,
        RunValidationResult run)
    {
        // Fixed-width payload: int64 + 32 bytes + int64 + 3×16 bytes + 32 bytes = 144 bytes
        var buffer = new byte[Int64Size + HashSize + Int64Size + GuidSize + GuidSize + GuidSize + HashSize];
        var offset = 0;

        WriteInt64(index, buffer, ref offset);
        WriteBytes(previousHash.AsSpan(), buffer, ref offset);
        WriteInt64(timestamp.UtcTicks, buffer, ref offset);
        WriteGuid(run.RunId, buffer, ref offset);
        WriteGuid(run.PlayerId, buffer, ref offset);
        WriteGuid(run.ServerId, buffer, ref offset);
        WriteBytes(run.FinalStateHash, buffer, ref offset);

        return ImmutableArray.Create(SHA256.HashData(buffer));
    }

    private static void WriteInt64(long value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination[offset..], value);
        offset += Int64Size;
    }

    private static void WriteBytes(ReadOnlySpan<byte> value, Span<byte> destination, ref int offset)
    {
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }

    private static void WriteGuid(Guid value, Span<byte> destination, ref int offset)
    {
        if (!value.TryWriteBytes(destination[offset..], bigEndian: true, out var bytesWritten) || bytesWritten != GuidSize)
        {
            throw new InvalidOperationException("Failed to write a 16-byte big-endian Guid into the ledger entry hash buffer.");
        }

        offset += bytesWritten;
    }

    private ImmutableArray<byte>? GetEntryHashAt(long index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return null;
        }

        return _entries[(int)index].EntryHash;
    }
}
