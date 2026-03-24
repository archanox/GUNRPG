using System.Buffers.Binary;
using System.Security.Cryptography;
using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Operators;

namespace GUNRPG.Ledger;

public sealed class RunLedgerMutation
{
    private const int GuidSize = 16;
    private const int Int32Size = sizeof(int);
    private const int Int64Size = sizeof(long);
    private const int SingleSize = sizeof(float);

    public static RunLedgerMutation Empty { get; } = new([], []);

    public RunLedgerMutation(
        IReadOnlyList<OperatorEvent> operatorEvents,
        IReadOnlyList<GameplayLedgerEvent> gameplayEvents)
    {
        OperatorEvents = operatorEvents ?? throw new ArgumentNullException(nameof(operatorEvents));
        GameplayEvents = gameplayEvents ?? throw new ArgumentNullException(nameof(gameplayEvents));

        if (OperatorEvents.Any(static evt => evt is null))
        {
            throw new ArgumentException("Operator event collections must not contain null entries.", nameof(operatorEvents));
        }

        if (GameplayEvents.Any(static evt => evt is null))
        {
            throw new ArgumentException("Gameplay event collections must not contain null entries.", nameof(gameplayEvents));
        }
    }

    public IReadOnlyList<OperatorEvent> OperatorEvents { get; }

    public IReadOnlyList<GameplayLedgerEvent> GameplayEvents { get; }

    public byte[] ComputeHash()
    {
        var operatorEventPayloads = OperatorEvents.Select(SerializeOperatorEvent).ToArray();
        var gameplayEventPayloads = GameplayEvents.Select(SerializeGameplayEvent).ToArray();
        var totalLength = sizeof(int);

        totalLength += operatorEventPayloads.Sum(static payload => sizeof(int) + payload.Length);
        totalLength += sizeof(int);
        totalLength += gameplayEventPayloads.Sum(static payload => sizeof(int) + payload.Length);

        var buffer = GC.AllocateUninitializedArray<byte>(totalLength);
        var offset = 0;

        WriteInt32(operatorEventPayloads.Length, buffer, ref offset);
        foreach (var payload in operatorEventPayloads)
        {
            WriteBytes(payload, buffer, ref offset);
        }

        WriteInt32(gameplayEventPayloads.Length, buffer, ref offset);
        foreach (var payload in gameplayEventPayloads)
        {
            WriteBytes(payload, buffer, ref offset);
        }

        return SHA256.HashData(buffer);
    }

    private static byte[] SerializeOperatorEvent(OperatorEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var eventTypeBytes = EncodeUtf8(evt.EventType);
        var payloadBytes = EncodeUtf8(evt.Payload);
        var previousHashBytes = EncodeUtf8(evt.PreviousHash);
        var hashBytes = EncodeUtf8(evt.Hash);
        var buffer = GC.AllocateUninitializedArray<byte>(
            GuidSize + Int64Size + (Int32Size * 4) + eventTypeBytes.Length + payloadBytes.Length + previousHashBytes.Length + hashBytes.Length + Int64Size);
        var offset = 0;
        WriteGuid(evt.OperatorId.Value, buffer, ref offset);
        WriteInt64(evt.SequenceNumber, buffer, ref offset);
        WriteBytes(eventTypeBytes, buffer, ref offset);
        WriteBytes(payloadBytes, buffer, ref offset);
        WriteBytes(previousHashBytes, buffer, ref offset);
        WriteBytes(hashBytes, buffer, ref offset);
        WriteInt64(evt.Timestamp.ToUnixTimeMilliseconds(), buffer, ref offset);
        return buffer;
    }

    private static byte[] SerializeGameplayEvent(GameplayLedgerEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        return evt switch
        {
            OperatorCreatedLedgerEvent created => SerializeGameplayEvent(1, evt.EventType, [EncodeUtf8(created.Name)]),
            RunCompletedLedgerEvent completed => SerializeGameplayEvent(2, evt.EventType, [EncodeBoolean(completed.WasSuccessful), EncodeUtf8(completed.Outcome)]),
            ItemAcquiredLedgerEvent acquired => SerializeGameplayEvent(3, evt.EventType, [EncodeUtf8(acquired.ItemId)]),
            ItemLostLedgerEvent lost => SerializeGameplayEvent(4, evt.EventType, [EncodeUtf8(lost.ItemId)]),
            PlayerDamagedLedgerEvent damaged => SerializeGameplayEvent(5, evt.EventType, [EncodeSingle(damaged.Amount), EncodeUtf8(damaged.Reason)]),
            PlayerHealedLedgerEvent healed => SerializeGameplayEvent(6, evt.EventType, [EncodeSingle(healed.Amount), EncodeUtf8(healed.Reason)]),
            XpAwardedLedgerEvent xp => SerializeGameplayEvent(7, evt.EventType, [EncodeInt64(xp.Amount), EncodeUtf8(xp.Reason)]),
            InfilStateChangedLedgerEvent infil => SerializeGameplayEvent(9, evt.EventType, [EncodeUtf8(infil.State), EncodeUtf8(infil.Reason)]),
            CombatSessionLedgerEvent combat => SerializeGameplayEvent(10, evt.EventType, [EncodeGuid(combat.SessionId), EncodeUtf8(combat.State)]),
            PetStateLedgerEvent pet => SerializeGameplayEvent(11, evt.EventType, [
                EncodeUtf8(pet.Action),
                EncodeSingle(pet.Health),
                EncodeSingle(pet.Fatigue),
                EncodeSingle(pet.Injury),
                EncodeSingle(pet.Stress),
                EncodeSingle(pet.Morale),
                EncodeSingle(pet.Hunger),
                EncodeSingle(pet.Hydration)]),
            EnemyDamagedLedgerEvent enemyDamaged => SerializeGameplayEvent(12, evt.EventType, [EncodeInt32(enemyDamaged.Amount), EncodeUtf8(enemyDamaged.Reason)]),
            _ => throw new InvalidOperationException($"Unsupported gameplay ledger event type {evt.GetType().Name}.")
        };
    }

    private static void WriteInt32(int value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination[offset..], value);
        offset += Int32Size;
    }

    private static void WriteInt64(long value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination[offset..], value);
        offset += Int64Size;
    }

    private static void WriteBytes(ReadOnlySpan<byte> value, Span<byte> destination, ref int offset)
    {
        WriteInt32(value.Length, destination, ref offset);
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }

    private static void WriteGuid(Guid value, Span<byte> destination, ref int offset)
    {
        if (!value.TryWriteBytes(destination[offset..], bigEndian: true, out var bytesWritten) || bytesWritten != GuidSize)
        {
            throw new InvalidOperationException(
                $"Failed to encode GUID {value:D} for run-ledger mutation hashing. Expected {GuidSize} bytes but wrote {bytesWritten}.");
        }

        offset += bytesWritten;
    }

    private static byte[] SerializeGameplayEvent(int typeId, string eventType, IReadOnlyList<byte[]> fields)
    {
        var eventTypeBytes = EncodeUtf8(eventType);
        var totalLength = Int32Size + Int32Size + eventTypeBytes.Length + fields.Sum(static field => Int32Size + field.Length);
        var buffer = GC.AllocateUninitializedArray<byte>(totalLength);
        var offset = 0;
        WriteInt32(typeId, buffer, ref offset);
        WriteBytes(eventTypeBytes, buffer, ref offset);

        foreach (var field in fields)
        {
            WriteBytes(field, buffer, ref offset);
        }

        return buffer;
    }

    private static byte[] EncodeUtf8(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return System.Text.Encoding.UTF8.GetBytes(value);
    }

    private static byte[] EncodeBoolean(bool value) => [value ? (byte)1 : (byte)0];

    private static byte[] EncodeInt32(int value)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(Int32Size);
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] EncodeSingle(float value)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(SingleSize);
        BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        return buffer;
    }

    private static byte[] EncodeInt64(long value)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(Int64Size);
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        return buffer;
    }

    private static byte[] EncodeGuid(Guid value)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(GuidSize);
        if (!value.TryWriteBytes(buffer, bigEndian: true, out var bytesWritten) || bytesWritten != GuidSize)
        {
            throw new InvalidOperationException(
                $"Failed to encode GUID {value:D} for run-ledger mutation hashing. Expected {GuidSize} bytes but wrote {bytesWritten}.");
        }

        return buffer;
    }
}
