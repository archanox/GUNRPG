using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace GUNRPG.Core.Simulation;

/// <summary>
/// A single player's action for a given tick, used in deterministic multi-player input batching.
/// </summary>
/// <param name="PlayerId">Unique identifier of the player submitting this action.</param>
/// <param name="Action">The player's action for this tick.</param>
public sealed record PlayerInput(Guid PlayerId, PlayerAction Action);

/// <summary>
/// A deterministically-ordered batch of player inputs for a single simulation tick.
/// Used to compute a canonical <see cref="InputHash"/> that is stable across all nodes
/// regardless of the order in which inputs were received.
/// </summary>
public sealed class TickInputs
{
    /// <summary>
    /// Initialises a new <see cref="TickInputs"/> with the given tick number and player inputs.
    /// The inputs are sorted deterministically by <see cref="PlayerInput.PlayerId"/> (big-endian
    /// byte order) so that every node produces the same hash for the same logical input set.
    /// </summary>
    public TickInputs(long tick, IReadOnlyList<PlayerInput> inputs)
    {
        if (inputs is null) throw new ArgumentNullException(nameof(inputs));

        Tick = tick;
        // §deterministic order: sort by PlayerId big-endian bytes, then by original index for stability.
        var sorted = inputs
            .Select((input, index) =>
            {
                if (input is null)
                    throw new ArgumentException($"Input at index {index} must not be null.", nameof(inputs));
                if (input.Action is null)
                    throw new ArgumentException($"Action at index {index} must not be null.", nameof(inputs));
                return (Input: input, Index: index);
            })
            .OrderBy(item => item.Input.PlayerId, PlayerIdComparer.Instance)
            .ThenBy(item => item.Index)
            .Select(item => item.Input)
            .ToArray();

        Inputs = sorted;
    }

    /// <summary>The simulation tick number.</summary>
    public long Tick { get; }

    /// <summary>
    /// Player inputs for this tick, sorted deterministically by <see cref="PlayerInput.PlayerId"/>.
    /// </summary>
    public IReadOnlyList<PlayerInput> Inputs { get; }

    /// <summary>
    /// Computes the SHA-256 hash of the canonical serialized form of this input batch.
    /// The encoding is: Tick (int64) || Count (int32), then for each input: PlayerId || ActionType || ActionPayload.
    /// </summary>
    public byte[] ComputeHash()
    {
        const int int32Size = 4;
        const int int64Size = 8;
        const int guidSize = 16;

        var buffer = new ArrayBufferWriter<byte>();

        // Tick
        var tickSpan = buffer.GetSpan(int64Size);
        BinaryPrimitives.WriteInt64BigEndian(tickSpan, Tick);
        buffer.Advance(int64Size);

        // Count
        var countSpan = buffer.GetSpan(int32Size);
        BinaryPrimitives.WriteInt32BigEndian(countSpan, Inputs.Count);
        buffer.Advance(int32Size);

        foreach (var input in Inputs)
        {
            // PlayerId (big-endian)
            var guidSpan = buffer.GetSpan(guidSize);
            if (!input.PlayerId.TryWriteBytes(guidSpan, bigEndian: true, out _))
                throw new InvalidOperationException("Failed to encode PlayerId for input hashing.");
            buffer.Advance(guidSize);

            // Action
            WriteAction(buffer, input.Action);
        }

        return SHA256.HashData(buffer.WrittenSpan);
    }

    private static void WriteAction(ArrayBufferWriter<byte> buffer, PlayerAction action)
    {
        const int int32Size = 4;
        const int guidSize = 16;

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
                throw new InvalidOperationException(
                    $"Cannot hash unknown action type '{action.GetType().Name}'.");
        }

        static void WriteInt32(ArrayBufferWriter<byte> buf, int value)
        {
            var span = buf.GetSpan(int32Size);
            BinaryPrimitives.WriteInt32BigEndian(span, value);
            buf.Advance(int32Size);
        }

        static void WriteGuid(ArrayBufferWriter<byte> buf, Guid id)
        {
            var span = buf.GetSpan(guidSize);
            if (!id.TryWriteBytes(span, bigEndian: true, out _))
                throw new InvalidOperationException("Failed to encode GUID for input hashing.");
            buf.Advance(guidSize);
        }
    }

    /// <summary>
    /// Comparer that orders <see cref="Guid"/> values by their big-endian byte representation,
    /// ensuring identical ordering on all platforms regardless of local endianness.
    /// </summary>
    private sealed class PlayerIdComparer : IComparer<Guid>
    {
        public static readonly PlayerIdComparer Instance = new();

        public int Compare(Guid x, Guid y)
        {
            Span<byte> xBytes = stackalloc byte[16];
            Span<byte> yBytes = stackalloc byte[16];
            x.TryWriteBytes(xBytes, bigEndian: true, out _);
            y.TryWriteBytes(yBytes, bigEndian: true, out _);
            return xBytes.SequenceCompareTo(yBytes);
        }
    }
}
