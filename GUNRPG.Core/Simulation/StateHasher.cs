using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GUNRPG.Core.Simulation;

public sealed class StateHasher : IStateHasher
{
    private const int GuidSize = 16;
    private const int Int32Size = 4;
    private const int Int64Size = 8;

    public byte[] HashTick(long tick, SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var buffer = new ArrayBufferWriter<byte>();
        WriteInt64(buffer, tick);
        WriteInt64(buffer, state.Time.CurrentTimeMs);
        WriteInt32(buffer, state.Random.Seed);
        WriteUInt64(buffer, state.Random.State);
        WriteInt32(buffer, state.Random.CallCount);
        WriteInt32(buffer, state.Player.Health);
        WriteInt32(buffer, state.Player.MaxHealth);
        // §2: Canonical ordering — sort enemies by Id for cross-machine determinism
        var sortedEnemies = state.Enemies.OrderBy(e => e.Id);
        WriteInt32(buffer, state.Enemies.Count);

        foreach (var enemy in sortedEnemies)
        {
            WriteInt32(buffer, enemy.Id);
            WriteInt32(buffer, enemy.Health);
            WriteInt32(buffer, enemy.MaxHealth);
        }

        WriteInt32(buffer, state.LastStepEvents.Count);
        foreach (var evt in state.LastStepEvents)
        {
            WriteString(buffer, evt.EventType);
            switch (evt)
            {
                case InfilStateChangedSimulationEvent infil:
                    WriteString(buffer, infil.State);
                    WriteString(buffer, infil.Reason);
                    break;
                case RunCompletedSimulationEvent completed:
                    WriteBoolean(buffer, completed.WasSuccessful);
                    WriteString(buffer, completed.Outcome);
                    break;
                case ItemAcquiredSimulationEvent item:
                    WriteString(buffer, item.ItemId);
                    break;
                case PlayerDamagedSimulationEvent damaged:
                    WriteInt32(buffer, damaged.Amount);
                    WriteString(buffer, damaged.Reason);
                    break;
                case PlayerHealedSimulationEvent healed:
                    WriteInt32(buffer, healed.Amount);
                    WriteString(buffer, healed.Reason);
                    break;
                case EnemyDamagedSimulationEvent enemyDamaged:
                    WriteInt32(buffer, enemyDamaged.Amount);
                    WriteString(buffer, enemyDamaged.Reason);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown simulation event '{evt.GetType().Name}'.");
            }
        }

        return SHA256.HashData(buffer.WrittenSpan);
    }

    public byte[] HashReplay(InputLog inputLog, IReadOnlyList<byte[]> tickHashes)
    {
        ArgumentNullException.ThrowIfNull(inputLog);
        ArgumentNullException.ThrowIfNull(tickHashes);

        var buffer = new ArrayBufferWriter<byte>();
        WriteGuid(buffer, inputLog.RunId);
        WriteGuid(buffer, inputLog.PlayerId);
        WriteInt32(buffer, inputLog.Seed);
        WriteInt32(buffer, inputLog.Entries.Count);

        foreach (var entry in inputLog.Entries)
        {
            WriteInt64(buffer, entry.Tick);
            WriteAction(buffer, entry.Action);
        }

        WriteInt32(buffer, tickHashes.Count);
        foreach (var tickHash in tickHashes)
        {
            WriteBytes(buffer, tickHash);
        }

        return SHA256.HashData(buffer.WrittenSpan);
    }

    private static void WriteAction(ArrayBufferWriter<byte> buffer, PlayerAction action)
    {
        switch (action)
        {
            case MoveAction move:
                WriteInt32(buffer, 1);
                WriteInt32(buffer, (int)move.Direction);
                break;
            case AttackAction attack:
                WriteInt32(buffer, 2);
                WriteGuid(buffer, attack.TargetId);
                break;
            case UseItemAction useItem:
                WriteInt32(buffer, 3);
                WriteGuid(buffer, useItem.ItemId);
                break;
            case ExfilAction:
                WriteInt32(buffer, 4);
                break;
            default:
                throw new InvalidOperationException($"Cannot hash unknown action type '{action.GetType().Name}'.");
        }
    }

    private static void WriteBoolean(ArrayBufferWriter<byte> buffer, bool value)
    {
        var span = buffer.GetSpan(1);
        span[0] = value ? (byte)1 : (byte)0;
        buffer.Advance(1);
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

    private static void WriteUInt64(ArrayBufferWriter<byte> buffer, ulong value)
    {
        var span = buffer.GetSpan(Int64Size);
        BinaryPrimitives.WriteUInt64BigEndian(span, value);
        buffer.Advance(Int64Size);
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

    private static void WriteString(ArrayBufferWriter<byte> buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        WriteBytes(buffer, bytes);
    }

    private static void WriteBytes(ArrayBufferWriter<byte> buffer, ReadOnlySpan<byte> bytes)
    {
        WriteInt32(buffer, bytes.Length);
        var span = buffer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        buffer.Advance(bytes.Length);
    }
}
