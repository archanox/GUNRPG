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
    private const int SingleSize = 4;

    /// <summary>
    /// Computes a state-based FinalHash from the key deterministic simulation-output fields
    /// of a completed session snapshot. Unlike <see cref="ComputeHash"/> which hashes replay
    /// inputs, this method hashes the simulation <em>outcome</em>, ensuring that the simulation
    /// is deterministic: the same inputs must produce the same final state, and therefore the
    /// same hash.
    /// </summary>
    /// <param name="finalSnapshot">
    /// The snapshot produced by running the full replay to completion.
    /// </param>
    public static byte[] ComputeStateHash(CombatSessionSnapshot finalSnapshot)
    {
        ArgumentNullException.ThrowIfNull(finalSnapshot);

        if (finalSnapshot.Player == null)
            throw new ArgumentException("Cannot compute state hash: Player is null.", nameof(finalSnapshot));
        if (finalSnapshot.Enemy == null)
            throw new ArgumentException("Cannot compute state hash: Enemy is null.", nameof(finalSnapshot));

        var buffer = new ArrayBufferWriter<byte>();
        // Session identity anchors the hash to this specific session.
        WriteGuid(buffer, finalSnapshot.Id);
        WriteInt32(buffer, finalSnapshot.Seed);
        // Version is included so future algorithm changes can be distinguished.
        WriteInt32(buffer, finalSnapshot.Version);
        WriteString(buffer, finalSnapshot.BalanceSnapshotVersion);
        WriteString(buffer, finalSnapshot.BalanceSnapshotHash);
        // Simulation outcome: how many turns were played and how the session ended.
        WriteInt32(buffer, finalSnapshot.TurnNumber);
        WriteInt32(buffer, (int)finalSnapshot.Phase);
        // Player final state.
        WriteSingle(buffer, finalSnapshot.Player.Health);
        WriteSingle(buffer, finalSnapshot.Player.MaxHealth);
        WriteInt32(buffer, finalSnapshot.Player.CurrentAmmo);
        // Enemy final state.
        WriteSingle(buffer, finalSnapshot.Enemy.Health);
        WriteSingle(buffer, finalSnapshot.Enemy.MaxHealth);
        WriteInt32(buffer, finalSnapshot.Enemy.CurrentAmmo);

        return SHA256.HashData(buffer.WrittenSpan);
    }

    /// <summary>
    /// Computes the canonical FinalHash for a session given its replay-critical data.
    /// The result is fully deterministic: same inputs always produce the same hash.
    /// Used as a fallback for legacy sessions that do not have a
    /// <see cref="CombatSessionSnapshot.ReplayInitialSnapshotJson"/> recorded.
    /// </summary>
    public static byte[] ComputeHash(
        Guid sessionId,
        int seed,
        int version,
        string? balanceSnapshotVersion,
        string? balanceSnapshotHash,
        int turnCount,
        IReadOnlyList<IntentSnapshot> replayTurns)
    {
        ArgumentNullException.ThrowIfNull(replayTurns);

        var buffer = new ArrayBufferWriter<byte>();
        WriteGuid(buffer, sessionId);
        WriteInt32(buffer, seed);
        WriteInt32(buffer, version);
        WriteString(buffer, balanceSnapshotVersion);
        WriteString(buffer, balanceSnapshotHash);
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

    private static void WriteString(ArrayBufferWriter<byte> buffer, string? value)
    {
        var encoded = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteInt32(buffer, encoded.Length);
        var span = buffer.GetSpan(encoded.Length);
        encoded.CopyTo(span);
        buffer.Advance(encoded.Length);
    }

    private static void WriteSingle(ArrayBufferWriter<byte> buffer, float value)
    {
        var span = buffer.GetSpan(SingleSize);
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        buffer.Advance(SingleSize);
    }
}
