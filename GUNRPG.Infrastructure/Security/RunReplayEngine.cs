using GUNRPG.Application.Backend;
using GUNRPG.Core.Operators;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GUNRPG.Security;

public sealed class RunReplayEngine
{
    private readonly ILogger<RunReplayEngine> _logger;

    public RunReplayEngine(ILogger<RunReplayEngine>? logger = null)
    {
        _logger = logger ?? NullLogger<RunReplayEngine>.Instance;
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
}
