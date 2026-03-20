using GUNRPG.Application.Backend;
using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Simulation;
using GUNRPG.Core.Operators;
using GUNRPG.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GUNRPG.Security;

public sealed class RunReplayEngine : IRunReplayEngine
{
    private readonly ILogger<RunReplayEngine> _logger;
    private readonly ServerIdentity? _serverIdentity;
    private readonly ReplayRunner _replayRunner;

    public RunReplayEngine(ILogger<RunReplayEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<RunReplayEngine>.Instance;
        _replayRunner = new ReplayRunner();
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

        var replay = _replayRunner.Replay(InputLog.FromRunInput(input));
        var events = replay.FinalState.Events.Select(MapGameplayEvent).ToArray();

        var mutation = new RunLedgerMutation([], events);
        var finalStateHash = replay.FinalHash;
        var attestation = new SignedRunValidation(
            serverIdentity.SignRunValidation(input.RunId, input.PlayerId, finalStateHash),
            serverIdentity.Certificate);

        _logger.LogDebug(
            "Run {RunId} replayed deterministically for player {PlayerId}: {ActionCount} action(s), {EventCount} event(s)",
            input.RunId,
            input.PlayerId,
            input.Actions.Count,
            events.Length);

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

    private static GameplayLedgerEvent MapGameplayEvent(SimulationEvent simulationEvent)
    {
        return simulationEvent switch
        {
            InfilStateChangedSimulationEvent infil => new InfilStateChangedLedgerEvent(infil.State, infil.Reason),
            RunCompletedSimulationEvent completed => new RunCompletedLedgerEvent(completed.WasSuccessful, completed.Outcome),
            ItemAcquiredSimulationEvent item => new ItemAcquiredLedgerEvent(item.ItemId),
            PlayerDamagedSimulationEvent damaged => new PlayerDamagedLedgerEvent(damaged.Amount, damaged.Reason),
            PlayerHealedSimulationEvent healed => new PlayerHealedLedgerEvent(healed.Amount, healed.Reason),
            EnemyDamagedSimulationEvent enemyDamaged => new EnemyDamagedLedgerEvent(enemyDamaged.Amount, enemyDamaged.Reason),
            _ => throw new InvalidOperationException($"Unknown simulation event '{simulationEvent.GetType().Name}'.")
        };
    }
}
