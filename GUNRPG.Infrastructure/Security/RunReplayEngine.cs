using GUNRPG.Application.Backend;
using GUNRPG.Core.Operators;

namespace GUNRPG.Security;

public sealed class RunReplayEngine
{
    public byte[] ValidateRunOnly(
        Guid runId,
        Guid playerId,
        IReadOnlyList<OperatorEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var replayedAggregate = OperatorAggregate.FromEvents(events);
        return OfflineMissionHashing.ComputeReplayFinalStateHash(replayedAggregate);
    }

    public RunValidationResult ValidateAndSignRun(
        Guid runId,
        Guid playerId,
        IReadOnlyList<OperatorEvent> events,
        ServerIdentity serverIdentity)
    {
        ArgumentNullException.ThrowIfNull(serverIdentity);

        var finalStateHash = ValidateRunOnly(runId, playerId, events);
        var attestation = new SignedRunValidation(
            serverIdentity.SignRunValidation(runId, playerId, finalStateHash),
            serverIdentity.Certificate);

        Console.WriteLine($"Run {runId} validated and signed by server {serverIdentity.Certificate.ServerId}");

        return new RunValidationResult(
            runId,
            playerId,
            serverIdentity.Certificate.ServerId,
            finalStateHash,
            attestation);
    }
}
