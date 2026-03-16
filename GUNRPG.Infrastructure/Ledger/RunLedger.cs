using System.Collections.ObjectModel;
using System.Security.Cryptography;
using GUNRPG.Security;

namespace GUNRPG.Ledger;

public class RunLedger
{
    private const int HashSize = SHA256.HashSizeInBytes;
    private const int GuidSize = 16;
    private const int Int64Size = 8;

    private static readonly byte[] ZeroHash = new byte[HashSize];

    private readonly List<RunLedgerEntry> _entries = [];
    private readonly ReadOnlyCollection<RunLedgerEntry> _readOnlyEntries;

    public RunLedger()
    {
        _readOnlyEntries = _entries.AsReadOnly();
    }

    public IReadOnlyList<RunLedgerEntry> Entries => _readOnlyEntries;

    public RunLedgerEntry? Head => _entries.Count == 0 ? null : _entries[^1];

    public RunLedgerEntry Append(RunValidationResult run)
    {
        return Append(run, DateTimeOffset.UtcNow);
    }

    internal RunLedgerEntry Append(RunValidationResult run, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(run);

        var index = (long)_entries.Count;
        var previousHash = _entries.Count == 0
            ? ZeroHash
            : (byte[])_entries[^1].EntryHash.Clone();

        var entryHash = ComputeEntryHash(index, previousHash, timestamp, run);

        var entry = new RunLedgerEntry(index, previousHash, entryHash, timestamp, run);
        _entries.Add(entry);
        return entry;
    }

    public bool Verify()
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];

            if (entry.Index != i)
            {
                return false;
            }

            if (entry.EntryHash.Length != HashSize || entry.PreviousHash.Length != HashSize)
            {
                return false;
            }

            var recomputed = ComputeEntryHash(entry.Index, entry.PreviousHash, entry.Timestamp, entry.Run);
            if (!CryptographicOperations.FixedTimeEquals(entry.EntryHash, recomputed))
            {
                return false;
            }

            var expectedPreviousHash = i == 0 ? ZeroHash : _entries[i - 1].EntryHash;
            if (expectedPreviousHash.Length != HashSize)
            {
                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(entry.PreviousHash, expectedPreviousHash))
            {
                return false;
            }
        }

        return true;
    }

    internal static byte[] ComputeEntryHash(
        long index,
        byte[] previousHash,
        DateTimeOffset timestamp,
        RunValidationResult run)
    {
        // Fixed-width payload: int64 + 32 bytes + int64 + 3×16 bytes + 32 bytes = 144 bytes
        var buffer = new byte[Int64Size + HashSize + Int64Size + GuidSize + GuidSize + GuidSize + HashSize];
        var offset = 0;

        WriteInt64(index, buffer, ref offset);
        WriteBytes(previousHash, buffer, ref offset);
        WriteInt64(timestamp.UtcTicks, buffer, ref offset);
        WriteGuid(run.RunId, buffer, ref offset);
        WriteGuid(run.PlayerId, buffer, ref offset);
        WriteGuid(run.ServerId, buffer, ref offset);
        WriteBytes(run.FinalStateHash, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    private static void WriteInt64(long value, Span<byte> destination, ref int offset)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(destination[offset..], value);
        offset += Int64Size;
    }

    private static void WriteBytes(byte[] value, Span<byte> destination, ref int offset)
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
}
