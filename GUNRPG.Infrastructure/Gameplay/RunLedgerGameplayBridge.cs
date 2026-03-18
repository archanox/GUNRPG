using GUNRPG.Application.Gameplay;
using GUNRPG.Core.Operators;
using GUNRPG.Gossip;
using GUNRPG.Ledger;
using GUNRPG.Security;

namespace GUNRPG.Infrastructure.Gameplay;

public sealed class RunLedgerGameplayBridge : IGameplayLedgerBridge
{
    private readonly RunLedger _ledger;
    private readonly IGameStateProjector _projector;
    private readonly IRunReplayEngine _replayEngine;
    private readonly IGossipService _gossipService;
    private readonly Authority _authority;
    private readonly byte[] _authorityPrivateKey;

    public RunLedgerGameplayBridge(
        RunLedger ledger,
        IGameStateProjector projector,
        IRunReplayEngine replayEngine,
        IGossipService gossipService,
        Authority authority,
        byte[] authorityPrivateKey)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _replayEngine = replayEngine ?? throw new ArgumentNullException(nameof(replayEngine));
        _gossipService = gossipService ?? throw new ArgumentNullException(nameof(gossipService));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _authorityPrivateKey = authorityPrivateKey ?? throw new ArgumentNullException(nameof(authorityPrivateKey));
    }

    public async Task MirrorAsync(
        Guid runId,
        OperatorId operatorId,
        IReadOnlyList<OperatorEvent> operatorEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorEvents);

        var input = new RunInput
        {
            RunId = runId,
            PlayerId = operatorId.Value,
            Actions = [],
            OperatorEvents = operatorEvents
        };

        var replayed = _replayEngine.Replay(input);
        var signedResult = AttachAuthoritySignature(replayed);
        await _gossipService.MergePartialValidationAsync(input, signedResult, cancellationToken).ConfigureAwait(false);
    }

    public Task<OperatorAggregate?> LoadProjectedOperatorAsync(OperatorId operatorId, CancellationToken cancellationToken = default)
    {
        var events = _ledger.Entries
            .SelectMany(static entry => entry.Run?.Mutation.OperatorEvents ?? [])
            .Where(evt => evt.OperatorId == operatorId)
            .OrderBy(evt => evt.SequenceNumber)
            .ToArray();

        if (events.Length == 0)
        {
            return Task.FromResult<OperatorAggregate?>(null);
        }

        return Task.FromResult<OperatorAggregate?>(OperatorAggregate.FromEvents(events));
    }

    public Task<IReadOnlyList<OperatorId>> ListProjectedOperatorsAsync(CancellationToken cancellationToken = default)
    {
        var ids = _ledger.Entries
            .SelectMany(static entry => entry.Run?.Mutation.OperatorEvents ?? [])
            .Select(static evt => evt.OperatorId)
            .Distinct()
            .OrderBy(static id => id.Value)
            .ToArray();

        return Task.FromResult<IReadOnlyList<OperatorId>>(ids);
    }

    public Task<GameState> ProjectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_projector.Project(_ledger.Entries));
    }

    private RunValidationResult AttachAuthoritySignature(RunValidationResult replayed)
    {
        var resultHash = replayed.Attestation.ResultHash;
        var signature = new AuthoritySignature(
            _authority.PublicKey,
            AuthorityCrypto.SignHashedPayload(_authorityPrivateKey, resultHash));

        var attestation = new SignedRunValidation(replayed.Attestation.Validation, replayed.Attestation.Certificate)
        {
            Signatures = [signature]
        };

        return new RunValidationResult(
            replayed.RunId,
            replayed.PlayerId,
            replayed.ServerId,
            replayed.FinalStateHash,
            attestation,
            replayed.Mutation);
    }
}
