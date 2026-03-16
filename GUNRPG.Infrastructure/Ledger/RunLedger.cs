using System.Security.Cryptography;
using GUNRPG.Security;

namespace GUNRPG.Ledger;

public class RunLedger
{
    private const int HashSize = SHA256.HashSizeInBytes;
    private readonly List<RunLedgerEntry> _entries = [];

    public IReadOnlyList<RunLedgerEntry> Entries => _entries;

    public RunLedgerEntry Append(RunValidationResult run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var index = (long)_entries.Count;
        var previousHash = _entries.Count == 0
            ? new byte[HashSize]
            : (byte[])_entries[^1].EntryHash.Clone();

        var timestamp = DateTimeOffset.UtcNow;
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

            var recomputed = ComputeEntryHash(entry.Index, entry.PreviousHash, entry.Timestamp, entry.Run);
            if (!CryptographicOperations.FixedTimeEquals(entry.EntryHash, recomputed))
            {
                return false;
            }

            var expectedPreviousHash = i == 0 ? new byte[HashSize] : _entries[i - 1].EntryHash;
            if (!CryptographicOperations.FixedTimeEquals(entry.PreviousHash, expectedPreviousHash))
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ComputeEntryHash(
        long index,
        byte[] previousHash,
        DateTimeOffset timestamp,
        RunValidationResult run)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(index);
        writer.Write(previousHash);
        writer.Write(timestamp.UtcDateTime.Ticks);
        WriteGuid(writer, run.RunId);
        WriteGuid(writer, run.PlayerId);
        WriteGuid(writer, run.ServerId);
        writer.Write(run.FinalStateHash);
        writer.Flush();

        return SHA256.HashData(ms.ToArray());
    }

    private static void WriteGuid(BinaryWriter writer, Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        writer.Write(bytes);
    }
}
