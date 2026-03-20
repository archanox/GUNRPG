using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// Computes a deterministic SHA-256 hash over a session's replay-critical fields.
/// Used to produce and verify <see cref="CombatSession.FinalHash"/>.
/// </summary>
public static class CombatSessionHasher
{
    private const int GuidSize = 16;
    private const int Int32Size = 4;
    private const int Int64Size = 8;

    /// <summary>
    /// Computes the canonical FinalHash for a session given its replay-critical data.
    /// The result is fully deterministic: same inputs always produce the same hash.
    /// </summary>
    public static byte[] ComputeHash(
        Guid sessionId,
        int seed,
        int version,
        int turnCount,
        IReadOnlyList<IntentSnapshot> replayTurns)
    {
        ArgumentNullException.ThrowIfNull(replayTurns);

        var buffer = new ArrayBufferWriter<byte>();
        WriteGuid(buffer, sessionId);
        WriteInt32(buffer, seed);
        WriteInt32(buffer, version);
        WriteInt32(buffer, turnCount);
        WriteInt32(buffer, replayTurns.Count);

        foreach (var turn in replayTurns)
        {
            WriteGuid(buffer, turn.OperatorId);
            WriteInt32(buffer, (int)turn.Primary);
            WriteInt32(buffer, (int)turn.Movement);
            WriteInt32(buffer, (int)turn.Stance);
            WriteInt32(buffer, (int)turn.Cover);
            WriteBoolean(buffer, turn.CancelMovement);
            WriteInt64(buffer, turn.SubmittedAtMs);
        }

        return SHA256.HashData(buffer.WrittenSpan);
    }

    private static void WriteGuid(ArrayBufferWriter<byte> buffer, Guid value)
    {
        var span = buffer.GetSpan(GuidSize);
        if (!value.TryWriteBytes(span, bigEndian: true, out var bytesWritten) || bytesWritten != GuidSize)
        {
            throw new InvalidOperationException("Failed to encode GUID for deterministic hashing.");
        }

        buffer.Advance(GuidSize);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> buffer, int value)
    {
        var span = buffer.GetSpan(Int32Size);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        buffer.Advance(Int32Size);
    }

    private static void WriteInt64(ArrayBufferWriter<byte> buffer, long value)
    {
        var span = buffer.GetSpan(Int64Size);
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        buffer.Advance(Int64Size);
    }

    private static void WriteBoolean(ArrayBufferWriter<byte> buffer, bool value)
    {
        var span = buffer.GetSpan(1);
        span[0] = value ? (byte)1 : (byte)0;
        buffer.Advance(1);
    }
}
