using System.Buffers.Binary;
using System.Security.Cryptography;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Operators;
using GUNRPG.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GUNRPG.Security;

public sealed class RunReplayEngine : IRunReplayEngine
{
    private const int GuidSize = 16;
    private const int Int64Size = 8;
    private const int Int32Size = 4;

    // Base hit-chance used when processing AttackActions.
    private const double BaseHitChance = 0.6;
    private const float MinAttackDamage = 10f;
    private const float MaxAttackDamage = 30f;
    private const float AttackDamageRange = MaxAttackDamage - MinAttackDamage;

    private readonly ILogger<RunReplayEngine> _logger;
    private readonly ServerIdentity? _serverIdentity;

    public RunReplayEngine(ILogger<RunReplayEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<RunReplayEngine>.Instance;
    }

    public RunReplayEngine(ServerIdentity serverIdentity, ILogger<RunReplayEngine>? logger = null)
        : this(logger)
    {
        _serverIdentity = serverIdentity ?? throw new ArgumentNullException(nameof(serverIdentity));
    }

    public RunValidationResult Replay(RunInput input)
    {
        if (_serverIdentity is null)
        {
            throw new InvalidOperationException("A server identity is required to replay and sign run input.");
        }

        return Replay(input, _serverIdentity);
    }

    public byte[] ValidateRunOnly(IReadOnlyList<OperatorEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var replayedAggregate = OperatorAggregate.FromEvents(events);
        if (replayedAggregate.Events.Count != events.Count)
        {
            throw new InvalidOperationException(
                $"Replay consumed only {replayedAggregate.Events.Count} of {events.Count} events; the event chain may be tampered.");
        }

        return OfflineMissionHashing.ComputeReplayFinalStateHash(replayedAggregate);
    }

    public RunValidationResult Replay(RunInput input, ServerIdentity serverIdentity)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(serverIdentity);

        // Deterministic RNG seeded from RunInput — same seed always produces same events.
        var rng = new Random(input.Seed);
        var events = ProcessActions(input.Actions, rng);

        var mutation = new RunLedgerMutation([], events);
        var finalStateHash = ComputeReplayHash(input, events);
        var attestation = new SignedRunValidation(
            serverIdentity.SignRunValidation(input.RunId, input.PlayerId, finalStateHash),
            serverIdentity.Certificate);

        _logger.LogDebug(
            "Run {RunId} replayed deterministically for player {PlayerId}: {ActionCount} action(s), {EventCount} event(s)",
            input.RunId,
            input.PlayerId,
            input.Actions.Count,
            events.Count);

        return new RunValidationResult(
            input.RunId,
            input.PlayerId,
            serverIdentity.Certificate.ServerId,
            finalStateHash,
            attestation,
            mutation);
    }

    public RunValidationResult ValidateAndSignRun(
        Guid runId,
        Guid playerId,
        IReadOnlyList<OperatorEvent> events,
        ServerIdentity serverIdentity)
    {
        ArgumentNullException.ThrowIfNull(serverIdentity);

        var finalStateHash = ValidateRunOnly(events);
        var attestation = new SignedRunValidation(
            serverIdentity.SignRunValidation(runId, playerId, finalStateHash),
            serverIdentity.Certificate);

        _logger.LogDebug("Run {RunId} validated and signed by server {ServerId}", runId, serverIdentity.Certificate.ServerId);

        return new RunValidationResult(
            runId,
            playerId,
            serverIdentity.Certificate.ServerId,
            finalStateHash,
            attestation);
    }

    /// <summary>
    /// Processes player actions sequentially using a seeded, deterministic RNG to simulate all
    /// gameplay effects (combat, damage, loot, status changes) and returns the resulting events.
    /// </summary>
    private static IReadOnlyList<GameplayLedgerEvent> ProcessActions(
        IReadOnlyList<PlayerAction> actions,
        Random rng)
    {
        var events = new List<GameplayLedgerEvent>(capacity: actions.Count);

        foreach (var action in actions)
        {
            switch (action)
            {
                case MoveAction move:
                    events.Add(new InfilStateChangedLedgerEvent("Moving", move.Direction.ToString()));
                    break;

                case AttackAction attack:
                    // Combat: seeded RNG determines hit outcome.
                    if (rng.NextDouble() < BaseHitChance)
                    {
                        var damage = MinAttackDamage + (float)(rng.NextDouble() * AttackDamageRange);
                        events.Add(new PlayerDamagedLedgerEvent(damage, attack.TargetId.ToString("N")));
                    }
                    break;

                case UseItemAction useItem:
                    events.Add(new ItemAcquiredLedgerEvent(useItem.ItemId.ToString("N")));
                    break;

                case ExfilAction:
                    events.Add(new RunCompletedLedgerEvent(true, "Exfil"));
                    break;
            }
        }

        return events;
    }

    private static byte[] ComputeReplayHash(RunInput input, IReadOnlyList<GameplayLedgerEvent> events)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Actions);

        var actionPayloads = new byte[input.Actions.Count][];
        // RunId (16) + PlayerId (16) + Seed (4) + action-count (4)
        var bufferLength = GuidSize + GuidSize + Int32Size + Int32Size;

        for (var i = 0; i < input.Actions.Count; i++)
        {
            var action = input.Actions[i] ?? throw new ArgumentException(
                $"Action at index {i} in run input must not be null.", nameof(input));
            var payload = SerializeAction(action);
            actionPayloads[i] = payload;
            bufferLength += Int32Size + payload.Length;
        }

        var buffer = GC.AllocateUninitializedArray<byte>(bufferLength);
        var offset = 0;
        WriteGuid(input.RunId, buffer, ref offset);
        WriteGuid(input.PlayerId, buffer, ref offset);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), input.Seed);
        offset += Int32Size;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), actionPayloads.Length);
        offset += Int32Size;

        foreach (var payload in actionPayloads)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), payload.Length);
            offset += Int32Size;
            payload.CopyTo(buffer, offset);
            offset += payload.Length;
        }

        // Include a hash of the derived events so the final hash covers all gameplay outcomes.
        var eventsHash = new RunLedgerMutation([], events).ComputeHash();
        var expanded = GC.AllocateUninitializedArray<byte>(buffer.Length + Int32Size + eventsHash.Length);
        buffer.CopyTo(expanded, 0);
        offset = buffer.Length;
        BinaryPrimitives.WriteInt32BigEndian(expanded.AsSpan(offset, Int32Size), eventsHash.Length);
        offset += Int32Size;
        eventsHash.CopyTo(expanded, offset);

        return SHA256.HashData(expanded);
    }

    private static byte[] SerializeAction(PlayerAction action)
    {
        return action switch
        {
            MoveAction move => SerializeMoveAction(move),
            AttackAction attack => SerializeAttackAction(attack),
            UseItemAction useItem => SerializeUseItemAction(useItem),
            ExfilAction => SerializeExfilAction(),
            _ => throw new InvalidOperationException($"Cannot serialize unknown action type '{action.GetType().Name}'.")
        };
    }

    private static byte[] SerializeMoveAction(MoveAction move)
    {
        // tag (1) + Direction as int (4)
        var buffer = GC.AllocateUninitializedArray<byte>(1 + Int32Size);
        buffer[0] = 1;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(1, Int32Size), (int)move.Direction);
        return buffer;
    }

    private static byte[] SerializeAttackAction(AttackAction attack)
    {
        // tag (1) + TargetId (16)
        var buffer = GC.AllocateUninitializedArray<byte>(1 + GuidSize);
        buffer[0] = 2;
        WriteGuidToSpan(attack.TargetId, buffer.AsSpan(1, GuidSize));
        return buffer;
    }

    private static byte[] SerializeUseItemAction(UseItemAction useItem)
    {
        // tag (1) + ItemId (16)
        var buffer = GC.AllocateUninitializedArray<byte>(1 + GuidSize);
        buffer[0] = 3;
        WriteGuidToSpan(useItem.ItemId, buffer.AsSpan(1, GuidSize));
        return buffer;
    }

    private static byte[] SerializeExfilAction()
    {
        // tag (1) only
        return [4];
    }

    private static void WriteGuid(Guid value, byte[] buffer, ref int offset)
    {
        value.TryWriteBytes(buffer.AsSpan(offset, GuidSize), bigEndian: true, out var bytesWritten);
        if (bytesWritten != GuidSize)
        {
            throw new InvalidOperationException(
                $"Expected {GuidSize} bytes when encoding GUID for replay hashing, but wrote {bytesWritten} bytes.");
        }

        offset += GuidSize;
    }

    private static void WriteGuidToSpan(Guid value, Span<byte> destination)
    {
        if (!value.TryWriteBytes(destination, bigEndian: true, out var bytesWritten) || bytesWritten != GuidSize)
        {
            throw new InvalidOperationException(
                $"Expected {GuidSize} bytes when encoding GUID for replay hashing.");
        }
    }
}
