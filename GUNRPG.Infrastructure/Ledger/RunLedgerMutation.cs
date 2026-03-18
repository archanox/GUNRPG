using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using GUNRPG.Core.Operators;

namespace GUNRPG.Ledger;

public sealed class RunLedgerMutation
{
    public static RunLedgerMutation Empty { get; } = new([], []);

    public RunLedgerMutation(
        IReadOnlyList<OperatorEvent> operatorEvents,
        IReadOnlyList<GameplayLedgerEvent> gameplayEvents)
    {
        OperatorEvents = operatorEvents ?? throw new ArgumentNullException(nameof(operatorEvents));
        GameplayEvents = gameplayEvents ?? throw new ArgumentNullException(nameof(gameplayEvents));
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
        var text = string.Join("|",
            evt.OperatorId.Value.ToString("N"),
            evt.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            evt.EventType,
            evt.Payload,
            evt.PreviousHash,
            evt.Hash,
            evt.Timestamp.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Encoding.UTF8.GetBytes(text);
    }

    private static byte[] SerializeGameplayEvent(GameplayLedgerEvent evt)
    {
        var text = evt switch
        {
            OperatorCreatedLedgerEvent created => $"{evt.EventType}|{created.Name}",
            RunCompletedLedgerEvent completed => $"{evt.EventType}|{completed.WasSuccessful}|{completed.Outcome}",
            ItemAcquiredLedgerEvent acquired => $"{evt.EventType}|{acquired.ItemId}",
            ItemLostLedgerEvent lost => $"{evt.EventType}|{lost.ItemId}",
            PlayerDamagedLedgerEvent damaged => $"{evt.EventType}|{damaged.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{damaged.Reason}",
            PlayerHealedLedgerEvent healed => $"{evt.EventType}|{healed.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{healed.Reason}",
            XpAwardedLedgerEvent xp => $"{evt.EventType}|{xp.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{xp.Reason}",
            PerkUnlockedLedgerEvent perk => $"{evt.EventType}|{perk.PerkName}",
            InfilStateChangedLedgerEvent infil => $"{evt.EventType}|{infil.State}|{infil.Reason}",
            CombatSessionLedgerEvent combat => $"{evt.EventType}|{combat.SessionId:N}|{combat.State}",
            PetStateLedgerEvent pet => string.Join("|",
                evt.EventType,
                pet.Action,
                pet.Health.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pet.Fatigue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pet.Injury.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pet.Stress.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pet.Morale.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pet.Hunger.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pet.Hydration.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw new ArgumentException($"Unsupported gameplay ledger event type {evt.GetType().Name}.", nameof(evt))
        };

        return Encoding.UTF8.GetBytes(text);
    }

    private static void WriteInt32(int value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination[offset..], value);
        offset += sizeof(int);
    }

    private static void WriteBytes(ReadOnlySpan<byte> value, Span<byte> destination, ref int offset)
    {
        WriteInt32(value.Length, destination, ref offset);
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }
}
