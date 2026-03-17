using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace GUNRPG.Ledger.Indexing;

public sealed class MerkleSkipIndex
{
    private const long PeriodicCheckpointInterval = 64;

    private readonly Dictionary<long, ImmutableArray<byte>> _index = [];
    private readonly ReadOnlyDictionary<long, ImmutableArray<byte>> _readOnlyIndex;
    private readonly Func<long, ImmutableArray<byte>?> _entryHashProvider;

    internal MerkleSkipIndex(Func<long, ImmutableArray<byte>?> entryHashProvider)
    {
        ArgumentNullException.ThrowIfNull(entryHashProvider);
        _entryHashProvider = entryHashProvider;
        _readOnlyIndex = new ReadOnlyDictionary<long, ImmutableArray<byte>>(_index);
    }

    public MerkleSkipIndex(IReadOnlyList<RunLedgerEntry> entries)
        : this(CreateEntryHashProvider(entries))
    {
        Rebuild(entries);
    }

    public IReadOnlyDictionary<long, ImmutableArray<byte>> Checkpoints => _readOnlyIndex;

    public long HighestIndex { get; private set; } = -1;

    public void Append(RunLedgerEntry entry)
    {
        RecordEntry(entry);
    }

    public void Update(RunLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RecordEntry(entry);
    }

    public void Rebuild(IReadOnlyList<RunLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _index.Clear();
        HighestIndex = -1;

        foreach (var entry in entries)
        {
            Append(entry);
        }
    }

    public long FindDivergenceIndex(MerkleSkipIndex peerIndex)
    {
        ArgumentNullException.ThrowIfNull(peerIndex);

        if (HighestIndex < 0 || peerIndex.HighestIndex < 0)
        {
            return 0;
        }

        var commonHighestIndex = Math.Min(HighestIndex, peerIndex.HighestIndex);
        if (commonHighestIndex < 0)
        {
            return 0;
        }

        if (!EntryHashesMatch(0, peerIndex))
        {
            return 0;
        }

        var lowerBound = 1L;
        var upperBound = commonHighestIndex + 1;

        for (var checkpoint = HighestPowerOfTwoAtMost(commonHighestIndex); checkpoint >= 1; checkpoint >>= 1)
        {
            if (!TryGetCheckpointHash(checkpoint, out var localHash) ||
                !peerIndex.TryGetCheckpointHash(checkpoint, out var peerHash))
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(localHash.AsSpan(), peerHash.AsSpan()))
            {
                lowerBound = checkpoint + 1;
                break;
            }

            upperBound = checkpoint;
        }

        for (var checkpoint = HighestPeriodicCheckpointAtMost(upperBound - 1);
             checkpoint >= lowerBound && checkpoint >= PeriodicCheckpointInterval;
             checkpoint -= PeriodicCheckpointInterval)
        {
            if (!TryGetCheckpointHash(checkpoint, out var localHash) ||
                !peerIndex.TryGetCheckpointHash(checkpoint, out var peerHash))
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(localHash.AsSpan(), peerHash.AsSpan()))
            {
                lowerBound = checkpoint + 1;
                break;
            }

            upperBound = checkpoint;
        }

        while (lowerBound < upperBound)
        {
            var midpoint = lowerBound + ((upperBound - lowerBound) / 2);
            if (EntryHashesMatch(midpoint, peerIndex))
            {
                lowerBound = midpoint + 1;
            }
            else
            {
                upperBound = midpoint;
            }
        }

        if (lowerBound > commonHighestIndex && HighestIndex == peerIndex.HighestIndex)
        {
            return -1;
        }

        return lowerBound;
    }

    public bool TryGetCheckpointHash(long index, out ImmutableArray<byte> entryHash)
    {
        return _index.TryGetValue(index, out entryHash);
    }

    private bool EntryHashesMatch(long index, MerkleSkipIndex peerIndex)
    {
        var localHash = GetEntryHash(index);
        var peerHash = peerIndex.GetEntryHash(index);

        if (localHash is null || peerHash is null || localHash.Value.Length != peerHash.Value.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(localHash.Value.AsSpan(), peerHash.Value.AsSpan());
    }

    private ImmutableArray<byte>? GetEntryHash(long index)
    {
        if (_index.TryGetValue(index, out var checkpointHash))
        {
            return checkpointHash;
        }

        return _entryHashProvider(index);
    }

    private static long HighestPowerOfTwoAtMost(long value)
    {
        var power = 1L;
        while (power * 2 <= value)
        {
            power <<= 1;
        }

        return power;
    }

    private static long HighestPeriodicCheckpointAtMost(long value)
    {
        return value < PeriodicCheckpointInterval
            ? 0
            : value - (value % PeriodicCheckpointInterval);
    }

    private static bool IsPowerOfTwo(long value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    private static bool IsCheckpointIndex(long value)
    {
        return IsPowerOfTwo(value) || (value >= PeriodicCheckpointInterval && value % PeriodicCheckpointInterval == 0);
    }

    private static Func<long, ImmutableArray<byte>?> CreateEntryHashProvider(IReadOnlyList<RunLedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return index => index >= 0 && index < entries.Count ? entries[(int)index].EntryHash : null;
    }

    private void RecordEntry(RunLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        HighestIndex = Math.Max(HighestIndex, entry.Index);

        if (IsCheckpointIndex(entry.Index))
        {
            _index[entry.Index] = entry.EntryHash;
        }
    }
}
