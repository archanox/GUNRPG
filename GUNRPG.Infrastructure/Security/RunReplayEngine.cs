using GUNRPG.Application.Backend;
using GUNRPG.Core.Operators;

namespace GUNRPG.Security;

public sealed class RunReplayEngine
{
    public RunValidationResult ValidateAndSignRun(
        Guid runId,
        Guid playerId,
        IReadOnlyList<OperatorEvent> events,
        ServerIdentity serverIdentity)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(serverIdentity);

        var replayedAggregate = OperatorAggregate.FromEvents(events);
        var finalStateHash = OfflineMissionHashing.ComputeReplayFinalStateHash(replayedAggregate);
        var attestation = new SignedRunValidation(
            serverIdentity.SignRunValidation(runId, playerId, finalStateHash),
            serverIdentity.Certificate);

        Console.WriteLine($"Run {runId} validated and signed by server {serverIdentity.Certificate.ServerId}");

        return new RunValidationResult(runId, playerId, finalStateHash, attestation);
    }
}
