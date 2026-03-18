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

        var mutation = ComputeMutationFromOperatorEvents(input.OperatorEvents);
        var finalStateHash = ComputeReplayHash(input, mutation);
        var attestation = new SignedRunValidation(
            serverIdentity.SignRunValidation(input.RunId, input.PlayerId, finalStateHash),
            serverIdentity.Certificate);

        _logger.LogDebug("Run {RunId} replayed deterministically for player {PlayerId}", input.RunId, input.PlayerId);

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

    private static byte[] ComputeReplayHash(RunInput input, RunLedgerMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Actions);
        ArgumentNullException.ThrowIfNull(input.OperatorEvents);

        var actionPayloads = new byte[input.Actions.Count][];
        var bufferLength = GuidSize + GuidSize + Int32Size;

        for (var i = 0; i < input.Actions.Count; i++)
        {
            var action = input.Actions[i] ?? throw new ArgumentException($"Action at index {i} in run input must not be null.", nameof(input));
            var payload = SerializeAction(action);
            actionPayloads[i] = payload;
            bufferLength += Int32Size + payload.Length;
        }

        var buffer = GC.AllocateUninitializedArray<byte>(bufferLength);
        var offset = 0;
        WriteGuid(input.RunId, buffer, ref offset);
        WriteGuid(input.PlayerId, buffer, ref offset);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), actionPayloads.Length);
        offset += Int32Size;

        foreach (var payload in actionPayloads)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), payload.Length);
            offset += Int32Size;
            payload.CopyTo(buffer, offset);
            offset += payload.Length;
        }

        var mutationHash = mutation.ComputeHash();
        var expanded = GC.AllocateUninitializedArray<byte>(buffer.Length + Int32Size + mutationHash.Length);
        buffer.CopyTo(expanded, 0);
        offset = buffer.Length;
        BinaryPrimitives.WriteInt32BigEndian(expanded.AsSpan(offset, Int32Size), mutationHash.Length);
        offset += Int32Size;
        mutationHash.CopyTo(expanded, offset);

        return SHA256.HashData(expanded);
    }

    private static RunLedgerMutation ComputeMutationFromOperatorEvents(IReadOnlyList<OperatorEvent> operatorEvents)
    {
        if (operatorEvents.Count == 0)
        {
            return RunLedgerMutation.Empty;
        }

        var gameplayEvents = BuildSemanticEvents(operatorEvents).ToArray();
        return new RunLedgerMutation(operatorEvents, gameplayEvents);
    }

    private static IEnumerable<GameplayLedgerEvent> BuildSemanticEvents(IEnumerable<OperatorEvent> operatorEvents)
    {
        foreach (var evt in operatorEvents)
        {
            switch (evt)
            {
                case OperatorCreatedEvent created:
                    yield return new OperatorCreatedLedgerEvent(created.GetName());
                    break;
                case XpGainedEvent xp:
                    var (amount, reason) = xp.GetPayload();
                    yield return new XpAwardedLedgerEvent(amount, reason);
                    break;
                case WoundsTreatedEvent healed:
                    yield return new PlayerHealedLedgerEvent(healed.GetHealthRestored(), "TreatWounds");
                    break;
                case LoadoutChangedEvent loadout:
                    yield return new ItemAcquiredLedgerEvent(loadout.GetWeaponName());
                    break;
                case PerkUnlockedEvent perk:
                    yield return new PerkUnlockedLedgerEvent(perk.GetPerkName());
                    break;
                case CombatVictoryEvent:
                    yield return new RunCompletedLedgerEvent(true, "Victory");
                    break;
                case ExfilFailedEvent failed:
                    yield return new RunCompletedLedgerEvent(false, failed.GetReason());
                    break;
                case OperatorDiedEvent died:
                    yield return new PlayerDamagedLedgerEvent(100f, died.GetCauseOfDeath());
                    yield return new RunCompletedLedgerEvent(false, died.GetCauseOfDeath());
                    break;
                case InfilStartedEvent infilStarted:
                    var (sessionId, _, _) = infilStarted.GetPayload();
                    yield return new InfilStateChangedLedgerEvent("Started", "InfilStarted");
                    yield return new CombatSessionLedgerEvent(sessionId, "Infil");
                    break;
                case InfilEndedEvent infilEnded:
                    var (wasSuccessful, endedReason) = infilEnded.GetPayload();
                    yield return new InfilStateChangedLedgerEvent("Ended", endedReason);
                    yield return new RunCompletedLedgerEvent(wasSuccessful, endedReason);
                    break;
                case CombatSessionStartedEvent combatStarted:
                    yield return new CombatSessionLedgerEvent(combatStarted.GetPayload(), "Started");
                    break;
                case PetActionAppliedEvent petAction:
                    var (action, health, fatigue, injury, stress, morale, hunger, hydration, _) = petAction.GetPayload();
                    yield return new PetStateLedgerEvent(action, health, fatigue, injury, stress, morale, hunger, hydration);
                    break;
            }
        }
    }

    private static byte[] SerializeAction(PlayerAction action)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(Int64Size + (Int32Size * 4) + 1);
        var offset = 0;

        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset, Int64Size), action.SequenceNumber);
        offset += Int64Size;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), EncodeOptionalEnum(action.Primary));
        offset += Int32Size;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), EncodeOptionalEnum(action.Movement));
        offset += Int32Size;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), EncodeOptionalEnum(action.Stance));
        offset += Int32Size;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset, Int32Size), EncodeOptionalEnum(action.Cover));
        offset += Int32Size;
        buffer[offset] = action.CancelMovement ? (byte)1 : (byte)0;

        return buffer;
    }

    private static int EncodeOptionalEnum<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value.HasValue ? Convert.ToInt32(value.Value) : -1;

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
}
